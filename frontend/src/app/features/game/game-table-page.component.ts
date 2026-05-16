import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';
import { BriscolaIndicatorComponent } from './briscola-indicator.component';
import { ChatPanelComponent } from './chat-panel.component';
import { EndGameDialogComponent } from './end-game-dialog.component';
import { Card } from './game.models';
import { GameService } from './game.service';
import { MyHandComponent } from './my-hand.component';
import { OpponentAreaComponent } from './opponent-area.component';
import { ReconnectBannerComponent } from './reconnect-banner.component';
import { ScoreboardComponent } from './scoreboard.component';
import { StockComponent } from './stock.component';
import { TrickAreaComponent } from './trick-area.component';

interface OpponentSlot {
  seatIndex: number;
  isPartner: boolean;
}

@Component({
  selector: 'bri-game-table-page',
  standalone: true,
  imports: [
    I18nPipe,
    OpponentAreaComponent,
    MyHandComponent,
    TrickAreaComponent,
    StockComponent,
    BriscolaIndicatorComponent,
    ScoreboardComponent,
    ChatPanelComponent,
    ReconnectBannerComponent,
    EndGameDialogComponent,
  ],
  templateUrl: './game-table-page.component.html',
  styleUrl: './game-table-page.component.scss',
})
export class GameTablePageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly game = inject(GameService);
  private readonly toast = inject(ErrorToastService);
  private readonly i18n = inject(I18nService);

  readonly state = this.game.state;
  readonly legalMoves = this.game.legalMoves;
  readonly chatLog = this.game.chatLog;
  readonly disconnectDeadline = this.game.disconnectDeadline;
  readonly disconnects = this.game.disconnects;
  readonly lastFinished = this.game.lastFinished;

  readonly isReady = computed(() => this.state() !== null);
  readonly is4p = computed(() => this.state()?.mode === 'FourPlayerTeams');

  /**
   * v1 seat resolution: pick the seat whose hand size matches our myHand
   * length. When multiple seats tie, we fall back to the first match. The
   * backend snapshot doesn't yet ship `mySeatIndex`; until that lands this
   * heuristic is good enough for layout and active-seat highlighting.
   */
  readonly mySeatIndex = computed<number | null>(() => {
    const s = this.state();
    if (!s || !s.myHand) {
      return null;
    }
    const target = s.myHand.length;
    const idx = s.handCountsBySeat.findIndex((c) => c === target);
    return idx === -1 ? null : idx;
  });

  readonly opponentSlots = computed<OpponentSlot[]>(() => {
    const s = this.state();
    const me = this.mySeatIndex();
    if (!s || me === null) {
      return [];
    }
    const total = s.handCountsBySeat.length;
    const slots: OpponentSlot[] = [];
    for (let i = 0; i < total; i++) {
      if (i === me) continue;
      const isPartner = s.mode === 'FourPlayerTeams' && i % 2 === me % 2;
      slots.push({ seatIndex: i, isPartner });
    }
    return slots;
  });

  readonly disconnectedSeats = computed<ReadonlySet<number>>(
    () => new Set(this.disconnects().map((d) => d.seatIndex)),
  );

  readonly disconnectSeatIndex = computed<number | null>(() => {
    const list = this.disconnects();
    return list.length === 0 ? null : (list[0]?.seatIndex ?? null);
  });

  readonly myTurn = computed(() => {
    const s = this.state();
    const me = this.mySeatIndex();
    if (!s || me === null) {
      return false;
    }
    return s.nextToPlaySeat === me && (s.phase === 'Playing' || s.phase === 'LastHand');
  });

  readonly opponentCountFor = (seatIndex: number): number =>
    this.state()?.handCountsBySeat[seatIndex] ?? 0;

  ngOnInit(): void {
    const gameId = this.route.snapshot.paramMap.get('id');
    if (!gameId) {
      return;
    }
    this.game.connect(gameId).catch(() => {
      this.toast.error(this.i18n.t('game.errors.connectFailed'));
    });
  }

  ngOnDestroy(): void {
    void this.game.disconnect();
  }

  async onCardPlayed(card: Card): Promise<void> {
    try {
      await this.game.play(card);
    } catch {
      this.toast.error(this.i18n.t('game.errors.playFailed'));
    }
  }

  async onSendChat(text: string): Promise<void> {
    try {
      await this.game.sendChat(text);
    } catch {
      // Server-side rate-limit / spectator rejection comes back via the
      // invalidMove signal; no toast needed here.
    }
  }

  async onBackToLobby(): Promise<void> {
    await this.router.navigateByUrl('/lobby');
  }
}
