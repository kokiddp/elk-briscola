import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { CardSetService } from '../../card-sets/card-set.service';
import { AuthService } from '../../core/auth.service';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { MatchHistoryEntry, MatchHistoryPage } from '../../core/models';
import { I18nPipe } from '../../shared/i18n.pipe';
import { ChangeEmailFormComponent } from './change-email-form.component';
import { ChangePasswordFormComponent } from './change-password-form.component';
import { MatchHistoryService } from './history.service';

const HISTORY_PAGE_SIZE = 10;

@Component({
  selector: 'bri-profile',
  standalone: true,
  imports: [I18nPipe, ChangeEmailFormComponent, ChangePasswordFormComponent],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
})
export class ProfileComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly cardSets = inject(CardSetService);
  private readonly history = inject(MatchHistoryService);
  private readonly toast = inject(ErrorToastService);
  private readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

  readonly user = this.auth.currentUser;
  readonly manifests = this.cardSets.manifests;
  readonly activeSetId = this.cardSets.activeSetId;

  readonly displayName = computed(() => this.user()?.displayName ?? this.user()?.username ?? '');
  readonly pendingId = signal<string | null>(null);
  readonly ranking = computed(() => this.user()?.ranking ?? null);

  readonly historyPage = signal<MatchHistoryPage | null>(null);
  readonly historyLoading = signal(false);
  readonly historyError = signal(false);
  readonly historyPageSize = HISTORY_PAGE_SIZE;

  readonly totalPages = computed(() => {
    const page = this.historyPage();
    if (!page || page.totalCount === 0) {
      return 0;
    }
    return Math.ceil(page.totalCount / page.pageSize);
  });

  ngOnInit(): void {
    // Pull a fresh /me snapshot so the ranking widget reflects the result
    // of any games finished since login. The AuthService cache is filled
    // once on login/refresh and isn't auto-invalidated, so without this
    // the Elo / W / L / D counters can lag by an entire session.
    void this.auth.refreshMe();
    void this.loadHistory(1);
  }

  async loadHistory(page: number): Promise<void> {
    if (this.historyLoading()) {
      return;
    }
    this.historyLoading.set(true);
    this.historyError.set(false);
    try {
      const result = await this.history.loadPage(page, HISTORY_PAGE_SIZE);
      this.historyPage.set(result);
    } catch {
      this.historyError.set(true);
      this.toast.error(this.i18n.t('profile.history.error'));
    } finally {
      this.historyLoading.set(false);
    }
  }

  hasPrev(): boolean {
    return (this.historyPage()?.page ?? 1) > 1;
  }

  hasNext(): boolean {
    const p = this.historyPage();
    return !!p && p.page < this.totalPages();
  }

  trackByGameId(_index: number, entry: MatchHistoryEntry): string {
    return entry.gameId;
  }

  trackById(_index: number, manifest: { id: string }): string {
    return manifest.id;
  }

  /**
   * Translates the result envelope into a player-relative outcome key:
   * "win" / "loss" / "draw". 4p uses team math; 2p compares seats.
   */
  resultFor(entry: MatchHistoryEntry): 'win' | 'loss' | 'draw' {
    if (entry.outcomeKind === 'Draw') {
      return 'draw';
    }
    if (entry.winnerKey === null) {
      return 'draw';
    }
    if (entry.mode === 'FourPlayerTeams') {
      return entry.mySeatIndex % 2 === entry.winnerKey ? 'win' : 'loss';
    }
    return entry.mySeatIndex === entry.winnerKey ? 'win' : 'loss';
  }

  myScore(entry: MatchHistoryEntry): number {
    if (entry.mode === 'FourPlayerTeams' && entry.teamScores) {
      const team = entry.mySeatIndex % 2;
      return entry.teamScores[team] ?? 0;
    }
    return entry.seatScores[entry.mySeatIndex] ?? 0;
  }

  opponentScore(entry: MatchHistoryEntry): number {
    if (entry.mode === 'FourPlayerTeams' && entry.teamScores) {
      const team = entry.mySeatIndex % 2;
      const otherTeam = team === 0 ? 1 : 0;
      return entry.teamScores[otherTeam] ?? 0;
    }
    return entry.seatScores
      .filter((_, i) => i !== entry.mySeatIndex)
      .reduce((max, score) => Math.max(max, score), 0);
  }

  formatDate(iso: string | null): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '—';
    return d.toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  modeLabel(mode: MatchHistoryEntry['mode']): string {
    return this.i18n.t(
      mode === 'TwoPlayer' ? 'lobby.mode.twoPlayer' : 'lobby.mode.fourPlayerTeams',
    );
  }

  reasonLabel(reason: MatchHistoryEntry['reason']): string {
    return this.i18n.t(`game.end.reason.${reason}`);
  }

  isActive(id: string): boolean {
    return this.activeSetId() === id;
  }

  isPending(id: string): boolean {
    return this.pendingId() === id;
  }

  previewUrl(manifest: { id: string; preview: string }): string {
    return `/card-sets/${manifest.id}/${manifest.preview}`;
  }

  onPreviewError(event: Event): void {
    const img = event.target as HTMLImageElement | null;
    if (!img) return;
    const fallback = '/card-sets/placeholder/preview.svg';
    if (img.src.endsWith(fallback)) {
      return;
    }
    img.src = fallback;
  }

  async onSelect(id: string): Promise<void> {
    if (this.pendingId() || this.isActive(id)) {
      return;
    }
    this.pendingId.set(id);
    try {
      await this.cardSets.setActiveSet(id);
    } catch {
      this.toast.error(this.i18n.t('profile.cardSet.error'));
    } finally {
      this.pendingId.set(null);
    }
  }

  /**
   * Handlers for the security section. Both change-* endpoints bump the
   * server-side SecurityStamp, so the access token in flight is now
   * invalid. We could try to ride the refresh-token flow, but the
   * cleanest UX is: surface a toast, log the user out, push them to
   * /login. The next sign-in pulls fresh tokens against the updated
   * credentials.
   */
  readonly emailSubmitting = signal(false);
  async onEmailSubmit(payload: { currentPassword: string; newEmail: string }): Promise<void> {
    if (this.emailSubmitting()) return;
    this.emailSubmitting.set(true);
    try {
      await this.auth.changeEmail({
        currentPassword: payload.currentPassword,
        newEmail: payload.newEmail,
      });
      this.toast.success(this.i18n.t('profile.security.email.success'));
      await this.signOutAndRedirect();
    } catch (err) {
      this.toast.error(this.errorMessage(err, 'profile.security.email.failed'));
    } finally {
      this.emailSubmitting.set(false);
    }
  }

  readonly passwordSubmitting = signal(false);
  async onPasswordSubmit(payload: { currentPassword: string; newPassword: string }): Promise<void> {
    if (this.passwordSubmitting()) return;
    this.passwordSubmitting.set(true);
    try {
      await this.auth.changePassword({
        currentPassword: payload.currentPassword,
        newPassword: payload.newPassword,
      });
      this.toast.success(this.i18n.t('profile.security.password.success'));
      await this.signOutAndRedirect();
    } catch (err) {
      this.toast.error(this.errorMessage(err, 'profile.security.password.failed'));
    } finally {
      this.passwordSubmitting.set(false);
    }
  }

  private async signOutAndRedirect(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }

  /**
   * Surface the server's structured error code when one is available.
   * The backend's 4xx bodies are shaped `{ code: string, ... }` —
   * 'InvalidCurrentPassword' / 'EmailAlreadyTaken' / 'ChangePasswordFailed' / etc.
   * We map known codes to i18n keys; everything else falls back to the
   * provided generic key. Defensive: HTTP errors that come back without
   * a body still get the fallback.
   */
  private errorMessage(err: unknown, fallbackKey: string): string {
    const code = this.extractErrorCode(err);
    if (code === 'InvalidCurrentPassword') {
      return this.i18n.t('profile.security.errors.InvalidCurrentPassword');
    }
    if (code === 'EmailAlreadyTaken') {
      return this.i18n.t('profile.security.errors.EmailAlreadyTaken');
    }
    if (code === 'EmailRequired') {
      return this.i18n.t('profile.security.errors.EmailRequired');
    }
    return this.i18n.t(fallbackKey);
  }

  private extractErrorCode(err: unknown): string | null {
    if (typeof err !== 'object' || err === null) return null;
    const e = err as { error?: unknown; status?: number };
    if (typeof e.error === 'object' && e.error !== null) {
      const body = e.error as { code?: unknown };
      if (typeof body.code === 'string') return body.code;
    }
    return null;
  }
}
