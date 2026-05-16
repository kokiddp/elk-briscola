import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from './auth.service';
import { MeResponse, TokenResponse } from './models';

const REFRESH_TOKEN_STORAGE_KEY = 'elk-briscola.refreshToken';

function tokenResponse(opts: Partial<TokenResponse> = {}): TokenResponse {
  const now = Date.now();
  return {
    accessToken: 'access-1',
    accessTokenExpiresAt: new Date(now + 15 * 60_000).toISOString(),
    refreshToken: 'refresh-1',
    refreshTokenExpiresAt: new Date(now + 14 * 24 * 60 * 60_000).toISOString(),
    ...opts,
  };
}

function meResponse(opts: Partial<MeResponse> = {}): MeResponse {
  return {
    id: 'u1',
    username: 'alice',
    displayName: 'Alice',
    email: 'a@a',
    activeCardSetId: 'placeholder',
    ranking: { elo: 1000, wins: 0, losses: 0, draws: 0, gamesPlayed: 0, updatedAt: '' },
    ...opts,
  };
}

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), AuthService],
  });
  const auth = TestBed.inject(AuthService);
  const ctrl = TestBed.inject(HttpTestingController);
  return { auth, ctrl };
}

async function loginWith(
  auth: AuthService,
  ctrl: HttpTestingController,
  tokenOverride?: Partial<TokenResponse>,
): Promise<void> {
  const p = auth.login({ usernameOrEmail: 'alice', password: 'hunter2' });
  ctrl.expectOne('/api/v1/auth/login').flush(tokenResponse(tokenOverride));
  await Promise.resolve();
  ctrl.expectOne('/api/v1/me').flush(meResponse());
  await p;
}

describe('AuthService.login', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('stores access token + persists refresh + loads /me', async () => {
    const { auth, ctrl } = setup();
    const tokens = tokenResponse();

    const p = auth.login({ usernameOrEmail: 'alice', password: 'hunter2' });
    ctrl.expectOne('/api/v1/auth/login').flush(tokens);
    await Promise.resolve();
    ctrl.expectOne('/api/v1/me').flush(meResponse());
    await p;

    expect(auth.getAccessToken()).toBe('access-1');
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.currentUser()?.username).toBe('alice');
    const persisted = JSON.parse(localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY) ?? '{}');
    expect(persisted.refreshToken).toBe('refresh-1');
    ctrl.verify();
  });
});

describe('AuthService.register', () => {
  it('POSTs the registration payload and does not auto-authenticate', async () => {
    const { auth, ctrl } = setup();
    const p = auth.register({ username: 'bob', email: 'b@b', password: 'hunter2hunter' });
    const req = ctrl.expectOne('/api/v1/auth/register');
    expect(req.request.body).toEqual({
      username: 'bob',
      email: 'b@b',
      password: 'hunter2hunter',
    });
    req.flush(null, { status: 201, statusText: 'Created' });
    await p;
    expect(auth.isAuthenticated()).toBe(false);
    ctrl.verify();
  });
});

describe('AuthService.refresh', () => {
  beforeEach(() => localStorage.clear());

  it('returns null when no refresh token is available', async () => {
    const { auth } = setup();
    const token = await auth.refresh();
    expect(token).toBeNull();
  });

  it('coalesces concurrent refresh calls into one HTTP request', async () => {
    const { auth, ctrl } = setup();

    // Seed a usable refresh token via a login.
    await loginWith(auth, ctrl, {
      accessTokenExpiresAt: new Date(Date.now() - 1_000).toISOString(),
    });

    const a = auth.refresh();
    const b = auth.refresh();
    expect(a).toBe(b);

    ctrl.expectOne('/api/v1/auth/refresh').flush(tokenResponse({ accessToken: 'access-2' }));
    expect(await a).toBe('access-2');
    expect(await b).toBe('access-2');
    ctrl.verify();
  });

  it('clears all auth state when refresh fails', async () => {
    const { auth, ctrl } = setup();

    await loginWith(auth, ctrl);

    const r = auth.refresh();
    ctrl
      .expectOne('/api/v1/auth/refresh')
      .flush({ code: 'expired' }, { status: 401, statusText: 'Unauthorized' });
    expect(await r).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)).toBeNull();
    ctrl.verify();
  });
});

describe('AuthService.getAccessTokenAsync', () => {
  beforeEach(() => localStorage.clear());

  it('returns the cached token when not yet expired', async () => {
    const { auth, ctrl } = setup();
    await loginWith(auth, ctrl);
    expect(await auth.getAccessTokenAsync()).toBe('access-1');
    ctrl.verify();
  });

  it('refreshes when the access token is about to expire', async () => {
    const { auth, ctrl } = setup();
    await loginWith(auth, ctrl, {
      accessTokenExpiresAt: new Date(Date.now() + 1_000).toISOString(),
    });

    const p = auth.getAccessTokenAsync();
    ctrl.expectOne('/api/v1/auth/refresh').flush(tokenResponse({ accessToken: 'access-2' }));
    expect(await p).toBe('access-2');
    ctrl.verify();
  });
});

describe('AuthService.logout', () => {
  beforeEach(() => localStorage.clear());

  it('POSTs the refresh token, clears state, even if the server errors', async () => {
    const { auth, ctrl } = setup();
    await loginWith(auth, ctrl);

    const p = auth.logout();
    ctrl.expectOne('/api/v1/auth/logout').flush(null, { status: 500, statusText: 'fail' });
    await p;

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.getAccessToken()).toBeNull();
    expect(localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)).toBeNull();
    ctrl.verify();
  });

  it('clears local state even when no refresh token exists', async () => {
    const { auth, ctrl } = setup();
    await auth.logout();
    expect(auth.isAuthenticated()).toBe(false);
    ctrl.verify();
  });
});

describe('AuthService.changePassword', () => {
  it('POSTs change-password with the supplied payload', async () => {
    const { auth, ctrl } = setup();
    const p = auth.changePassword({ currentPassword: 'old', newPassword: 'newpass12' });
    const req = ctrl.expectOne('/api/v1/auth/change-password');
    expect(req.request.body).toEqual({ currentPassword: 'old', newPassword: 'newpass12' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await p;
    ctrl.verify();
  });
});

describe('AuthService persisted refresh token hydration', () => {
  afterEach(() => localStorage.clear());

  it('rehydrates a still-valid refresh token from storage on construction', () => {
    const future = new Date(Date.now() + 60_000).toISOString();
    localStorage.setItem(
      REFRESH_TOKEN_STORAGE_KEY,
      JSON.stringify({ refreshToken: 'r-persisted', refreshTokenExpiresAt: future }),
    );
    const { auth } = setup();
    expect(auth.hasUsableRefreshToken()).toBe(true);
  });

  it('discards an expired refresh token from storage on construction', () => {
    const past = new Date(Date.now() - 60_000).toISOString();
    localStorage.setItem(
      REFRESH_TOKEN_STORAGE_KEY,
      JSON.stringify({ refreshToken: 'r-expired', refreshTokenExpiresAt: past }),
    );
    const { auth } = setup();
    expect(auth.hasUsableRefreshToken()).toBe(false);
    expect(localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)).toBeNull();
  });
});
