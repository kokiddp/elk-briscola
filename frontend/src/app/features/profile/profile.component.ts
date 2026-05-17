import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CardSetService } from '../../card-sets/card-set.service';
import { AuthService } from '../../core/auth.service';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { MatchHistoryEntry, MatchHistoryPage } from '../../core/models';
import { I18nPipe } from '../../shared/i18n.pipe';
import { MatchHistoryService } from './history.service';

const HISTORY_PAGE_SIZE = 10;

@Component({
  selector: 'bri-profile',
  standalone: true,
  imports: [I18nPipe],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
})
export class ProfileComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly cardSets = inject(CardSetService);
  private readonly history = inject(MatchHistoryService);
  private readonly toast = inject(ErrorToastService);
  private readonly i18n = inject(I18nService);

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
}
