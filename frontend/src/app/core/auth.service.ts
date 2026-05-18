import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Observable, of, tap } from 'rxjs';
import {
  ChangePasswordRequest,
  LoginRequest,
  MeResponse,
  RegisterRequest,
  TokenResponse,
} from './models';

const REFRESH_TOKEN_STORAGE_KEY = 'elk-briscola.refreshToken';
const API_PREFIX = '/api/v1';

interface PersistedTokens {
  refreshToken: string | null;
  refreshTokenExpiresAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly accessToken = signal<string | null>(null);
  private readonly accessTokenExpiresAt = signal<Date | null>(null);
  private readonly refreshTokenSig = signal<string | null>(null);
  private readonly refreshTokenExpiresAt = signal<Date | null>(null);
  private readonly user = signal<MeResponse | null>(null);

  private refreshInFlight: Promise<string | null> | null = null;

  readonly currentUser = computed(() => this.user());
  readonly isAuthenticated = computed(() => !!this.accessToken() && !!this.user());

  constructor() {
    const persisted = this.readPersisted();
    if (persisted.refreshToken && persisted.refreshTokenExpiresAt) {
      const expires = new Date(persisted.refreshTokenExpiresAt);
      if (expires.getTime() > Date.now()) {
        this.refreshTokenSig.set(persisted.refreshToken);
        this.refreshTokenExpiresAt.set(expires);
      } else {
        this.clearPersisted();
      }
    }
  }

  getAccessToken(): string | null {
    return this.accessToken();
  }

  hasUsableRefreshToken(): boolean {
    const token = this.refreshTokenSig();
    const expires = this.refreshTokenExpiresAt();
    return !!token && !!expires && expires.getTime() > Date.now();
  }

  async getAccessTokenAsync(): Promise<string> {
    const current = this.accessToken();
    const exp = this.accessTokenExpiresAt();
    if (current && exp && exp.getTime() - Date.now() > 5_000) {
      return current;
    }
    const refreshed = await this.refresh();
    if (!refreshed) {
      throw new Error('No valid access token available');
    }
    return refreshed;
  }

  async login(req: LoginRequest): Promise<void> {
    const tokens = await firstValueFrom(
      this.http.post<TokenResponse>(`${API_PREFIX}/auth/login`, req),
    );
    this.applyTokens(tokens);
    await this.loadMe();
  }

  async register(req: RegisterRequest): Promise<void> {
    await firstValueFrom(this.http.post(`${API_PREFIX}/auth/register`, req));
  }

  async logout(): Promise<void> {
    const refreshToken = this.refreshTokenSig();
    if (refreshToken) {
      try {
        await firstValueFrom(this.http.post(`${API_PREFIX}/auth/logout`, { refreshToken }));
      } catch {
        // best-effort; clear local state regardless
      }
    }
    this.clearAll();
  }

  async changePassword(req: ChangePasswordRequest): Promise<void> {
    await firstValueFrom(this.http.post(`${API_PREFIX}/auth/change-password`, req));
  }

  refresh(): Promise<string | null> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }
    const refreshToken = this.refreshTokenSig();
    if (!refreshToken) {
      return Promise.resolve(null);
    }
    this.refreshInFlight = (async () => {
      try {
        const tokens = await firstValueFrom(
          this.http.post<TokenResponse>(`${API_PREFIX}/auth/refresh`, {
            refreshToken,
          }),
        );
        this.applyTokens(tokens);
        if (!this.user()) {
          await this.loadMe();
        }
        return tokens.accessToken;
      } catch {
        this.clearAll();
        return null;
      } finally {
        this.refreshInFlight = null;
      }
    })();
    return this.refreshInFlight;
  }

  loadMeIfNeeded$(): Observable<MeResponse | null> {
    if (this.user()) {
      return of(this.user());
    }
    return this.http.get<MeResponse>(`${API_PREFIX}/me`).pipe(tap((me) => this.user.set(me)));
  }

  /**
   * Patches the cached <c>currentUser().ranking</c> in place. Called by
   * the SignalR `RankingUpdated` push so the profile widget updates in
   * real time after a game finishes — no polling, no /me round-trip.
   * No-op when there's no cached user (the user logged out mid-game).
   */
  applyRanking(ranking: MeResponse['ranking']): void {
    const current = this.user();
    if (!current) {
      return;
    }
    this.user.set({ ...current, ranking });
  }

  /**
   * Forces a fresh GET /me + replaces the cached user snapshot. Use after
   * server-side changes that affect the cached payload — most notably
   * after a game finishes (Elo + W/L/D in the ranking widget would
   * otherwise stay stuck on whatever was cached at login time).
   *
   * Never throws — a network failure swallows the refresh and leaves the
   * stale snapshot in place. Callers are signal subscribers that re-read
   * on the next mount anyway.
   */
  async refreshMe(): Promise<MeResponse | null> {
    try {
      const me = await firstValueFrom(this.http.get<MeResponse>(`${API_PREFIX}/me`));
      this.user.set(me);
      return me;
    } catch {
      return this.user();
    }
  }

  private async loadMe(): Promise<void> {
    const me = await firstValueFrom(this.http.get<MeResponse>(`${API_PREFIX}/me`));
    this.user.set(me);
  }

  private applyTokens(tokens: TokenResponse): void {
    this.accessToken.set(tokens.accessToken);
    this.accessTokenExpiresAt.set(new Date(tokens.accessTokenExpiresAt));
    this.refreshTokenSig.set(tokens.refreshToken);
    this.refreshTokenExpiresAt.set(new Date(tokens.refreshTokenExpiresAt));
    this.persist({
      refreshToken: tokens.refreshToken,
      refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
    });
  }

  private clearAll(): void {
    this.accessToken.set(null);
    this.accessTokenExpiresAt.set(null);
    this.refreshTokenSig.set(null);
    this.refreshTokenExpiresAt.set(null);
    this.user.set(null);
    this.clearPersisted();
  }

  private persist(p: PersistedTokens): void {
    try {
      localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, JSON.stringify(p));
    } catch {
      // localStorage may be unavailable (private mode, SSR); silently ignore.
    }
  }

  private readPersisted(): PersistedTokens {
    try {
      const raw = localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
      if (!raw) {
        return { refreshToken: null, refreshTokenExpiresAt: null };
      }
      const parsed = JSON.parse(raw) as PersistedTokens;
      return {
        refreshToken: parsed.refreshToken ?? null,
        refreshTokenExpiresAt: parsed.refreshTokenExpiresAt ?? null,
      };
    } catch {
      return { refreshToken: null, refreshTokenExpiresAt: null };
    }
  }

  private clearPersisted(): void {
    try {
      localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
    } catch {
      // ignore
    }
  }
}
