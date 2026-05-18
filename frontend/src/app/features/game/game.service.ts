import { computed, inject, Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { AuthService } from '../../core/auth.service';
import type { MeResponse } from '../../core/models';
import { createHubConnection } from '../../core/signalr-client';
import {
  Card,
  cardKey,
  GameChatMessage,
  GameFinishedEvent,
  InvalidMoveCode,
  RedactedStateForUser,
} from './game.models';

const GAME_HUB_PATH = '/hubs/game';
const CHAT_BACKLOG_MAX = 500;

export interface SeatDisconnect {
  seatIndex: number;
  deadline: Date;
}

@Injectable({ providedIn: 'root' })
export class GameService {
  private readonly auth = inject(AuthService);

  private readonly stateSig = signal<RedactedStateForUser | null>(null);
  private readonly chatLogSig = signal<readonly GameChatMessage[]>([]);
  private readonly disconnectsSig = signal<readonly SeatDisconnect[]>([]);
  private readonly idleWarningsSig = signal<readonly SeatDisconnect[]>([]);
  private readonly lastInvalidMoveSig = signal<InvalidMoveCode | null>(null);
  private readonly lastFinishedSig = signal<GameFinishedEvent | null>(null);
  private readonly connectionStateSig = signal<HubConnectionState>(HubConnectionState.Disconnected);
  private readonly spectatorSig = signal(false);

  private connection: HubConnection | null = null;
  private connectPromise: Promise<void> | null = null;
  private currentGameId: string | null = null;
  private currentSpectator = false;

  readonly state = computed(() => this.stateSig());
  readonly chatLog = computed(() => this.chatLogSig());
  readonly disconnects = computed(() => this.disconnectsSig());
  readonly idleWarnings = computed(() => this.idleWarningsSig());
  readonly lastInvalidMove = computed(() => this.lastInvalidMoveSig());
  readonly lastFinished = computed(() => this.lastFinishedSig());
  readonly connectionState = computed(() => this.connectionStateSig());
  readonly isSpectator = computed(() => this.spectatorSig());

  readonly myHand = computed<Card[]>(() => this.stateSig()?.myHand ?? []);

  /**
   * v1 legal-moves rule: any card in hand is legal on the player's own turn,
   * once we're in a playable phase. The engine still authoritatively rejects
   * illegal moves via `invalidMove`.
   */
  readonly legalMoves = computed<Set<string>>(() => {
    const s = this.stateSig();
    const me = this.auth.currentUser();
    if (!s || !me || !s.myHand || s.myHand.length === 0) {
      return new Set();
    }
    if (s.phase !== 'Playing' && s.phase !== 'LastHand') {
      return new Set();
    }
    // Without a mySeat we cannot judge "my turn"; the snapshot's myHand is only
    // populated for the calling player so its presence + nextToPlaySeat is the
    // proxy. The component layer renders the disabled state.
    return new Set(s.myHand.map((c) => cardKey(c)));
  });

  readonly disconnectDeadline = computed<Date | null>(() => {
    const list = this.disconnectsSig();
    let earliest: Date | null = null;
    for (const d of list) {
      if (earliest === null || d.deadline < earliest) {
        earliest = d.deadline;
      }
    }
    return earliest;
  });

  async connect(gameId: string): Promise<void> {
    return this.connectInternal(gameId, false);
  }

  async connectAsSpectator(gameId: string): Promise<void> {
    return this.connectInternal(gameId, true);
  }

  private async connectInternal(gameId: string, spectator: boolean): Promise<void> {
    if (
      this.currentGameId === gameId &&
      this.currentSpectator === spectator &&
      this.connection &&
      this.connection.state === HubConnectionState.Connected
    ) {
      return;
    }
    if (this.connectPromise) {
      return this.connectPromise;
    }
    // Tear down any prior game's connection before opening a new one.
    if (this.connection || this.currentGameId !== gameId || this.currentSpectator !== spectator) {
      await this.disconnect();
    }
    this.currentGameId = gameId;
    this.currentSpectator = spectator;
    this.spectatorSig.set(spectator);
    this.resetGameState();

    // Build + own a LOCAL reference. An external disconnect() that fires
    // while start() is in flight will null `this.connection`; we must not
    // touch `this.connection.state` after that point.
    const conn = this.buildConnection();
    this.connection = conn;

    this.connectPromise = (async () => {
      try {
        await conn.start();
        // External disconnect won the race: stop and bail without throwing.
        if (this.connection !== conn) {
          await conn.stop().catch(() => undefined);
          return;
        }
        this.connectionStateSig.set(conn.state);
        if (conn.state === HubConnectionState.Connected) {
          await conn.invoke(spectator ? 'SpectateGame' : 'JoinGame', gameId);
        }
      } catch (err) {
        // If we still own the connection, drop it so the next connect()
        // rebuilds. If an external disconnect already swapped it out,
        // don't trample its bookkeeping.
        if (this.connection === conn) {
          this.connection = null;
          this.currentGameId = null;
          this.connectionStateSig.set(HubConnectionState.Disconnected);
        }
        await conn.stop().catch(() => undefined);
        throw err;
      } finally {
        this.connectPromise = null;
      }
    })();
    return this.connectPromise;
  }

  async disconnect(): Promise<void> {
    // Wait out any in-flight connect attempt so we don't race with start().
    if (this.connectPromise) {
      await this.connectPromise.catch(() => undefined);
    }
    const conn = this.connection;
    const gameId = this.currentGameId;
    const spectator = this.currentSpectator;
    // Clear the field before awaiting stop() — a concurrent connect() that
    // observes `this.connection !== local-conn` will see a fresh slate
    // instead of fighting the in-flight stop.
    this.connection = null;
    this.currentGameId = null;
    this.currentSpectator = false;
    this.spectatorSig.set(false);
    this.connectionStateSig.set(HubConnectionState.Disconnected);
    if (!conn) {
      return;
    }
    try {
      if (conn.state === HubConnectionState.Connected && gameId) {
        const leaveMethod = spectator ? 'UnspectateGame' : 'LeaveGame';
        await conn.invoke(leaveMethod, gameId).catch(() => undefined);
      }
      await conn.stop();
    } catch {
      // best-effort
    }
  }

  async play(card: Card): Promise<void> {
    if (!this.connection || !this.currentGameId) {
      return;
    }
    if (this.connection.state !== HubConnectionState.Connected) {
      return;
    }
    await this.connection.invoke('PlayCard', this.currentGameId, card);
  }

  async viewPile(): Promise<void> {
    if (!this.connection || !this.currentGameId) {
      return;
    }
    if (this.connection.state !== HubConnectionState.Connected) {
      return;
    }
    await this.connection.invoke('ViewOwnPile', this.currentGameId);
  }

  async sendChat(text: string): Promise<void> {
    const trimmed = text.trim();
    if (!trimmed || !this.connection || !this.currentGameId) {
      return;
    }
    if (this.connection.state !== HubConnectionState.Connected) {
      return;
    }
    await this.connection.invoke('SendChat', this.currentGameId, trimmed);
  }

  clearInvalidMove(): void {
    this.lastInvalidMoveSig.set(null);
  }

  private buildConnection(): HubConnection {
    const conn = createHubConnection(GAME_HUB_PATH, () => this.auth.getAccessTokenAsync());

    conn.on('joined', (snapshot: RedactedStateForUser) => this.applySnapshot(snapshot));
    conn.on('stateUpdated', (snapshot: RedactedStateForUser) => this.applySnapshot(snapshot));

    conn.on('phaseChanged', (newPhase: RedactedStateForUser['phase']) => {
      this.stateSig.update((s) => (s ? { ...s, phase: newPhase } : s));
    });

    conn.on('gameFinished', (evt: GameFinishedEvent) => {
      this.lastFinishedSig.set(evt);
      this.stateSig.update((s) =>
        s ? { ...s, phase: 'Finished', seatScores: evt.seatScores, outcome: evt.outcome } : s,
      );
    });

    conn.on('playerDisconnected', (seatIndex: number, graceDeadlineUtc: string) =>
      this.upsertDisconnect(seatIndex, graceDeadlineUtc),
    );
    conn.on('playerReconnected', (seatIndex: number) => this.removeDisconnect(seatIndex));
    conn.on('idleWarning', (seatIndex: number, forfeitDeadlineUtc: string) =>
      this.upsertIdleWarning(seatIndex, forfeitDeadlineUtc),
    );

    conn.on('chatMessage', (message: GameChatMessage) => this.appendChat(message));

    conn.on('invalidMove', (code: InvalidMoveCode) => this.lastInvalidMoveSig.set(code));

    // Real-time Elo / W / L / D push — fires once per affected player
    // right after GameFinished. The dispatcher targets the user's
    // personal connection so we never receive opponents' rankings.
    conn.on('rankingUpdated', (ranking: MeResponse['ranking']) => this.auth.applyRanking(ranking));

    conn.onreconnecting(() => this.connectionStateSig.set(HubConnectionState.Reconnecting));
    conn.onreconnected(async () => {
      this.connectionStateSig.set(HubConnectionState.Connected);
      if (this.currentGameId) {
        // Idempotent rejoin re-pushes the authoritative snapshot.
        const method = this.currentSpectator ? 'SpectateGame' : 'JoinGame';
        await conn.invoke(method, this.currentGameId).catch(() => undefined);
      }
    });
    conn.onclose(() => this.connectionStateSig.set(HubConnectionState.Disconnected));

    return conn;
  }

  private applySnapshot(snapshot: RedactedStateForUser): void {
    this.stateSig.set(snapshot);
    if (snapshot.outcome) {
      // GameFinished may arrive before or after a final snapshot; keep them in sync.
      this.stateSig.update((s) => (s ? { ...s, phase: 'Finished' } : s));
    }
  }

  private upsertDisconnect(seatIndex: number, isoDeadline: string): void {
    const deadline = new Date(isoDeadline);
    if (Number.isNaN(deadline.getTime())) {
      return;
    }
    this.disconnectsSig.update((list) =>
      replaceOrAppend(list, { seatIndex, deadline }, (d) => d.seatIndex === seatIndex),
    );
  }

  private removeDisconnect(seatIndex: number): void {
    this.disconnectsSig.update((list) => list.filter((d) => d.seatIndex !== seatIndex));
  }

  private upsertIdleWarning(seatIndex: number, isoDeadline: string): void {
    const deadline = new Date(isoDeadline);
    if (Number.isNaN(deadline.getTime())) {
      return;
    }
    this.idleWarningsSig.update((list) =>
      replaceOrAppend(list, { seatIndex, deadline }, (d) => d.seatIndex === seatIndex),
    );
  }

  private appendChat(message: GameChatMessage): void {
    this.chatLogSig.update((log) => {
      const next = [...log, message];
      return next.length > CHAT_BACKLOG_MAX ? next.slice(next.length - CHAT_BACKLOG_MAX) : next;
    });
  }

  private resetGameState(): void {
    this.stateSig.set(null);
    this.chatLogSig.set([]);
    this.disconnectsSig.set([]);
    this.idleWarningsSig.set([]);
    this.lastInvalidMoveSig.set(null);
    this.lastFinishedSig.set(null);
  }
}

function replaceOrAppend<T>(list: readonly T[], next: T, match: (item: T) => boolean): T[] {
  const idx = list.findIndex(match);
  if (idx === -1) {
    return [...list, next];
  }
  const out = list.slice();
  out[idx] = next;
  return out;
}
