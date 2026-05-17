import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';
import { BriscolaIndicatorComponent } from './briscola-indicator.component';
import { ChatPanelComponent } from './chat-panel.component';
import { EndGameDialogComponent } from './end-game-dialog.component';
import { Card, InvalidMoveCode } from './game.models';
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

const INVALID_MOVE_I18N: Record<InvalidMoveCode, string> = {
  NotYourTurn: 'game.invalidMove.notYourTurn',
  CardNotInHand: 'game.invalidMove.cardNotInHand',
  GameFinished: 'game.invalidMove.gameFinished',
  WrongPhase: 'game.invalidMove.wrongPhase',
  PileViewNotAllowed: 'game.invalidMove.pileViewNotAllowed',
  SpectatorsCannotChat: 'game.invalidMove.spectatorsCannotChat',
  RateLimited: 'game.invalidMove.rateLimited',
};

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
  readonly isSpectator = this.game.isSpectator;

  /**
   * v1 seat resolution. Latched once: the first snapshot that lets us derive
   * a unique seat (exactly one seat in `handCountsBySeat` matches the visible
   * hand length) sticks. Hand sizes drift in and out of alignment between
   * players as the game progresses, so we MUST NOT recompute per snapshot —
   * doing so would make the player and opponent swap visually. Known
   * follow-up: ship `mySeatIndex` directly in the wire snapshot to retire
   * this heuristic.
   */
  private readonly latchedSeat = signal<number | null>(null);
  readonly mySeatIndex = computed<number | null>(() => this.latchedSeat());

  readonly opponentSlots = computed<OpponentSlot[]>(() => {
    const s = this.state();
    const me = this.mySeatIndex();
    if (!s || me === null) {
      return [];
    }
    const total = s.handCountsBySeat.length;
    if (total <= 1) {
      return [];
    }
    // Rotate so the seat across (partner in 4p) is always the middle slot.
    // For 4p (total = 4) the order is [left, top, right] = [me+3, me+2, me+1]
    // mod 4. For 2p (total = 2) the only opponent is me+1 mod 2.
    const slots: OpponentSlot[] = [];
    for (let offset = total - 1; offset >= 1; offset--) {
      const seatIndex = (me + offset) % total;
      const isPartner = s.mode === 'FourPlayerTeams' && seatIndex % 2 === me % 2;
      slots.push({ seatIndex, isPartner });
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

  constructor() {
    // Latch mySeat the first time we can identify it unambiguously.
    effect(() => {
      if (this.latchedSeat() !== null) {
        return;
      }
      const s = this.state();
      if (!s) {
        return;
      }
      // Preferred path: the server tells us our seat directly.
      if (typeof s.mySeatIndex === 'number') {
        this.latchedSeat.set(s.mySeatIndex);
        return;
      }
      // Fallback path (older servers / spectators may omit mySeatIndex).
      if (!s.myHand) {
        return;
      }
      const target = s.myHand.length;
      let unique: number | null = null;
      for (let i = 0; i < s.handCountsBySeat.length; i++) {
        if (s.handCountsBySeat[i] === target) {
          if (unique !== null) {
            // Ambiguous: more than one seat matches. Wait for a later
            // snapshot where counts diverge before latching.
            return;
          }
          unique = i;
        }
      }
      if (unique !== null) {
        this.latchedSeat.set(unique);
      }
    });

    // Surface invalidMove rejections as a toast (i18n'd by code), then clear
    // the signal so the same code can fire again later. If WrongPhase fires
    // before we have any state, it almost certainly means the user landed
    // here before the game transitioned to Running — bounce them back to
    // the lobby instead of hanging on the connecting placeholder.
    effect(() => {
      const code = this.game.lastInvalidMove();
      if (!code) {
        return;
      }
      const key = INVALID_MOVE_I18N[code] ?? 'game.invalidMove.generic';
      this.toast.error(this.i18n.t(key));
      this.game.clearInvalidMove();
      if (code === 'WrongPhase' && this.state() === null) {
        void this.router.navigateByUrl('/lobby');
      }
    });
  }

  ngOnInit(): void {
    const gameId = this.route.snapshot.paramMap.get('id');
    if (!gameId) {
      return;
    }
    const spectator = this.route.snapshot.data['spectator'] === true;
    const start = spectator ? this.game.connectAsSpectator(gameId) : this.game.connect(gameId);
    start.catch(() => {
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
      // invalidMove signal — already handled by the toast effect above.
    }
  }

  async onBackToLobby(): Promise<void> {
    await this.router.navigateByUrl('/lobby');
  }
}
