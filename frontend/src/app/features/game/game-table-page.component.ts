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

type SeatPosition = 'top' | 'left' | 'right' | 'bottom';

interface OpponentSlot {
  seatIndex: number;
  isPartner: boolean;
  position: SeatPosition;
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
   * MyHand is visible only when the snapshot includes our hand (server-side
   * redaction sets myHand to null for spectators) and the page is not in
   * spectator mode. Two independent signals — server visibility and client
   * intent — must agree before the cards render.
   */
  readonly canSeeMyHand = computed(() => {
    if (this.isSpectator()) return false;
    const s = this.state();
    return s !== null && s.myHand !== null;
  });

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
    if (!s) {
      return [];
    }
    const total = s.handCountsBySeat.length;
    if (total <= 1) {
      return [];
    }
    // Spectators have no seat to anchor the rotation to. Show every
    // seat as an "opponent" slot in a stable layout: 2p → left/right,
    // 4p → top/left/right/bottom (seat 0 at the bottom, then clockwise).
    if (this.isSpectator()) {
      if (s.mode === 'TwoPlayer') {
        return [
          { seatIndex: 0, isPartner: false, position: 'left' },
          { seatIndex: 1, isPartner: false, position: 'right' },
        ];
      }
      return [
        { seatIndex: 0, isPartner: false, position: 'bottom' },
        { seatIndex: 1, isPartner: false, position: 'left' },
        { seatIndex: 2, isPartner: false, position: 'top' },
        { seatIndex: 3, isPartner: false, position: 'right' },
      ];
    }
    const me = this.mySeatIndex();
    if (me === null) {
      return [];
    }
    // Rotate so the seat across (partner in 4p) is always the middle slot.
    // For 4p (total = 4) the order is [left, top, right] = [me+3, me+2, me+1]
    // mod 4. For 2p (total = 2) the only opponent is me+1 mod 2.
    const slots: OpponentSlot[] = [];
    for (let offset = total - 1; offset >= 1; offset--) {
      const seatIndex = (me + offset) % total;
      const isPartner = s.mode === 'FourPlayerTeams' && seatIndex % 2 === me % 2;
      const position: SeatPosition =
        s.mode === 'TwoPlayer' ? 'top' : offset === 3 ? 'left' : offset === 2 ? 'top' : 'right';
      slots.push({ seatIndex, isPartner, position });
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

  /** Per-seat display name + Elo from the snapshot, or null. Used by
   *  the opponent area + the end-game dialog. */
  readonly playerFor = (seatIndex: number) => this.state()?.seatPlayers?.[seatIndex] ?? null;

  /** Active-seat forfeit deadline as a Date, parsed once per snapshot.
   *  The server resets this with every move, so the countdown the UI
   *  renders is a per-turn timer rather than a stale 90s-after-idle
   *  window. */
  readonly activeForfeitDeadline = computed<Date | null>(() => {
    const iso = this.state()?.activeSeatForfeitDeadline ?? null;
    if (!iso) {
      return null;
    }
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? null : d;
  });

  /** Forfeit deadline for `seatIndex` only when it's their turn; null
   *  otherwise so the chip stays on the seat that's actually thinking. */
  readonly idleDeadlineFor = (seatIndex: number): Date | null => {
    const s = this.state();
    if (!s || s.nextToPlaySeat !== seatIndex) {
      return null;
    }
    return this.activeForfeitDeadline();
  };

  /** Deadline for the local player, if it's their turn. Drives the
   *  countdown badge in the top-right of MyHand. */
  readonly myIdleDeadline = computed<Date | null>(() => {
    const me = this.mySeatIndex();
    const s = this.state();
    if (me === null || !s || s.nextToPlaySeat !== me) {
      return null;
    }
    return this.activeForfeitDeadline();
  });

  private readonly nowSig = signal(Date.now());
  readonly myIdleSecondsRemaining = computed<number | null>(() => {
    const dl = this.myIdleDeadline();
    if (!dl) {
      return null;
    }
    const ms = dl.getTime() - this.nowSig();
    return Math.max(0, Math.ceil(ms / 1000));
  });

  constructor() {
    // Tick the local-idle clock every second when there's a deadline to
    // count down to. Paused otherwise so it doesn't churn the change
    // detector during normal play.
    effect((onCleanup) => {
      if (!this.myIdleDeadline()) {
        return;
      }
      const id = setInterval(() => this.nowSig.set(Date.now()), 1000);
      onCleanup(() => clearInterval(id));
    });

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
