import { computed, inject, Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { AuthService } from '../../core/auth.service';
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

  private connection: HubConnection | null = null;
  private connectPromise: Promise<void> | null = null;
  private currentGameId: string | null = null;

  readonly state = computed(() => this.stateSig());
  readonly chatLog = computed(() => this.chatLogSig());
  readonly disconnects = computed(() => this.disconnectsSig());
  readonly idleWarnings = computed(() => this.idleWarningsSig());
  readonly lastInvalidMove = computed(() => this.lastInvalidMoveSig());
  readonly lastFinished = computed(() => this.lastFinishedSig());
  readonly connectionState = computed(() => this.connectionStateSig());

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
    if (
      this.currentGameId === gameId &&
      this.connection &&
      this.connection.state === HubConnectionState.Connected
    ) {
      return;
    }
    if (this.connectPromise) {
      return this.connectPromise;
    }
    // Tear down any prior game's connection before opening a new one.
    if (this.connection || this.currentGameId !== gameId) {
      await this.disconnect();
    }
    this.currentGameId = gameId;
    this.resetGameState();

    this.connectPromise = (async () => {
      try {
        this.connection = this.buildConnection();
        await this.connection.start();
        this.connectionStateSig.set(this.connection.state);
        if (this.connection.state === HubConnectionState.Connected) {
          await this.connection.invoke('JoinGame', gameId);
        }
      } catch (err) {
        const dead = this.connection;
        this.connection = null;
        this.currentGameId = null;
        this.connectionStateSig.set(HubConnectionState.Disconnected);
        if (dead) {
          await dead.stop().catch(() => undefined);
        }
        throw err;
      } finally {
        this.connectPromise = null;
      }
    })();
    return this.connectPromise;
  }

  async disconnect(): Promise<void> {
    const conn = this.connection;
    const gameId = this.currentGameId;
    if (!conn) {
      this.currentGameId = null;
      this.connectionStateSig.set(HubConnectionState.Disconnected);
      return;
    }
    try {
      if (conn.state === HubConnectionState.Connected && gameId) {
        await conn.invoke('LeaveGame', gameId).catch(() => undefined);
      }
      await conn.stop();
    } catch {
      // best-effort
    } finally {
      this.connection = null;
      this.currentGameId = null;
      this.connectionStateSig.set(HubConnectionState.Disconnected);
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

    conn.onreconnecting(() => this.connectionStateSig.set(HubConnectionState.Reconnecting));
    conn.onreconnected(async () => {
      this.connectionStateSig.set(HubConnectionState.Connected);
      if (this.currentGameId) {
        // Idempotent rejoin re-pushes the authoritative snapshot.
        await conn.invoke('JoinGame', this.currentGameId).catch(() => undefined);
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
