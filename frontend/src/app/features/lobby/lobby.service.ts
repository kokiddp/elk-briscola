import { HttpClient, HttpParams } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { createHubConnection } from '../../core/signalr-client';
import {
  CreateGameRequest,
  GameDetail,
  GameSummary,
  JoinGameRequest,
  LobbyChatMessage,
} from './lobby.models';

const API_PREFIX = '/api/v1';
const LOBBY_HUB_PATH = '/hubs/lobby';
const CHAT_BACKLOG_MAX = 200;

@Injectable({ providedIn: 'root' })
export class LobbyService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  constructor() {
    // Auto-connect to the LobbyHub whenever a user is authenticated.
    // The connection lives at the SPA-singleton level so pending-game
    // pushes (gameStarted, gameEnded) reach the user even if they
    // navigated away from /lobby to /profile / /home. Disconnects
    // explicitly on logout.
    effect(() => {
      const me = this.auth.currentUser();
      if (me) {
        // Best-effort — failures land via the existing error path.
        void this.connect().catch(() => undefined);
      } else if (this.connection) {
        void this.disconnect();
      }
    });

    // Stash the most recently observed pendingGameId. The auto-route
    // effect below needs it because onGameStarted removes the game
    // from openGames *before* lastStartedGameId fires — at the moment
    // the effect runs, the computed pendingGameId has already gone to
    // null and the comparison would lose the race.
    effect(() => {
      const id = this.pendingGameId();
      if (id) {
        this.lastSeenPendingGameId = id;
      }
    });

    // Global auto-route: whenever the user's pending game transitions
    // to Running, navigate them into the table from wherever they are.
    //
    // Exception: if the user is *already* on a /game/* route — playing
    // or spectating something else — don't yank them away. The audit
    // (H4) flagged the previous unconditional nav as a UX hazard: a
    // spectator watching one match would get teleported the moment a
    // separate pending game of theirs filled.
    effect(() => {
      const started = this.lastStartedGameIdSig();
      if (!started) {
        return;
      }
      if (started === this.lastSeenPendingGameId) {
        this.lastSeenPendingGameId = null;
        this.lastStartedGameIdSig.set(null);
        if (this.router.url.startsWith('/game/')) {
          // User already on a game route — leave them be. We do clear
          // the captured pendingGameId so a subsequent transition (a
          // new pending game) can route normally.
          return;
        }
        void this.router.navigateByUrl(`/game/${started}`);
      }
    });
  }

  private lastSeenPendingGameId: string | null = null;

  private readonly openGamesSig = signal<readonly GameSummary[]>([]);
  private readonly runningGamesSig = signal<readonly GameSummary[]>([]);
  private readonly chatLogSig = signal<readonly LobbyChatMessage[]>([]);
  private readonly connectionStateSig = signal<HubConnectionState>(HubConnectionState.Disconnected);
  private readonly lastStartedGameIdSig = signal<string | null>(null);
  private readonly lastEndedGameIdSig = signal<string | null>(null);

  private connection: HubConnection | null = null;
  private connectPromise: Promise<void> | null = null;

  readonly openGames = computed(() => this.openGamesSig());
  readonly runningGames = computed(() => this.runningGamesSig());
  readonly chatLog = computed(() => this.chatLogSig());
  readonly connectionState = computed(() => this.connectionStateSig());
  /** Last game id we saw transition Open → Running. Components watch this
   *  to auto-route into the table when their pending game starts. */
  readonly lastStartedGameId = computed(() => this.lastStartedGameIdSig());
  /** Last game id the server announced as ended (running finish OR the
   *  open-lobby janitor abandoned an unfilled game). The lobby component
   *  uses this to clear the creator's pending banner + toast them when
   *  their game gets reaped for being empty. */
  readonly lastEndedGameId = computed(() => this.lastEndedGameIdSig());

  /**
   * The open game (if any) that the current user is already seated at.
   * Derived directly from the openGames list + the auth'd user id, so
   * it survives navigating away from /lobby and refreshing the page:
   * on next connect() the open list re-loads from the server and the
   * computed fires again.
   *
   * Becomes `null` once the game transitions to Running (it drops out
   * of openGames) or is abandoned (same).
   */
  readonly pendingGame = computed<GameSummary | null>(() => {
    const me = this.auth.currentUser()?.id;
    if (!me) return null;
    return (
      this.openGamesSig().find((g) => (g.seatPlayers ?? []).some((p) => p?.userId === me)) ?? null
    );
  });
  readonly pendingGameId = computed<string | null>(() => this.pendingGame()?.id ?? null);
  readonly hasPendingGame = computed<boolean>(() => this.pendingGameId() !== null);

  /**
   * The Running game (if any) the current user is seated at. Mirrors
   * `pendingGame` for the post-start phase, so the top-nav can offer a
   * "resume" entry that takes them back to /game/:id from anywhere.
   */
  readonly currentRunningGame = computed<GameSummary | null>(() => {
    const me = this.auth.currentUser()?.id;
    if (!me) return null;
    return (
      this.runningGamesSig().find((g) => (g.seatPlayers ?? []).some((p) => p?.userId === me)) ??
      null
    );
  });
  readonly currentRunningGameId = computed<string | null>(
    () => this.currentRunningGame()?.id ?? null,
  );

  clearLastStartedGameId(): void {
    this.lastStartedGameIdSig.set(null);
  }

  clearLastEndedGameId(): void {
    this.lastEndedGameIdSig.set(null);
  }

  async connect(): Promise<void> {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      return;
    }
    if (this.connectPromise) {
      return this.connectPromise;
    }
    this.connectPromise = (async () => {
      try {
        await this.refreshLists();
        this.connection ??= this.buildConnection();
        if (this.connection.state === HubConnectionState.Disconnected) {
          await this.connection.start();
        }
        this.connectionStateSig.set(this.connection.state);
        if (this.connection.state === HubConnectionState.Connected) {
          await this.connection.invoke('SubscribeOpen');
        }
      } catch (err) {
        // Drop the dead connection so the next connect() rebuilds it.
        const dead = this.connection;
        this.connection = null;
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
    if (!conn) {
      this.connectionStateSig.set(HubConnectionState.Disconnected);
      return;
    }
    try {
      if (conn.state === HubConnectionState.Connected) {
        await conn.invoke('UnsubscribeOpen').catch(() => undefined);
      }
      await conn.stop();
    } catch {
      // ignore: best-effort teardown
    } finally {
      // Clear the field *after* stop so a concurrent connect() doesn't spin up
      // a second HubConnection while the first is still tearing down.
      this.connection = null;
      this.connectionStateSig.set(HubConnectionState.Disconnected);
    }
  }

  async refreshLists(): Promise<void> {
    const [open, running] = await Promise.all([this.fetchList('Open'), this.fetchList('Running')]);
    this.openGamesSig.set(open);
    this.runningGamesSig.set(running);
  }

  async createGame(req: CreateGameRequest): Promise<GameDetail> {
    return firstValueFrom(this.http.post<GameDetail>(`${API_PREFIX}/games`, req));
  }

  async joinGame(gameId: string, password?: string | null): Promise<GameDetail> {
    const body: JoinGameRequest = { password: password ?? null };
    return firstValueFrom(this.http.post<GameDetail>(`${API_PREFIX}/games/${gameId}/join`, body));
  }

  async leaveGame(gameId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${API_PREFIX}/games/${gameId}/leave`, {}));
  }

  async sendChat(text: string): Promise<void> {
    const trimmed = text.trim();
    if (!trimmed || !this.connection) {
      return;
    }
    if (this.connection.state !== HubConnectionState.Connected) {
      return;
    }
    await this.connection.invoke('SendChat', trimmed);
  }

  private fetchList(status: 'Open' | 'Running'): Promise<GameSummary[]> {
    const params = new HttpParams().set('status', status);
    return firstValueFrom(this.http.get<GameSummary[]>(`${API_PREFIX}/games`, { params }));
  }

  private buildConnection(): HubConnection {
    const conn = createHubConnection(LOBBY_HUB_PATH, () => this.auth.getAccessTokenAsync());

    conn.on('gameCreated', (summary: GameSummary) => this.onGameCreated(summary));
    conn.on('gameUpdated', (summary: GameSummary) => this.onGameUpdated(summary));
    conn.on('gameStarted', (gameId: string) => this.onGameStarted(gameId));
    conn.on('gameEnded', (gameId: string) => this.onGameEnded(gameId));
    conn.on('chatMessage', (msg: LobbyChatMessage) => this.onChatMessage(msg));

    conn.onreconnecting(() => this.connectionStateSig.set(HubConnectionState.Reconnecting));
    conn.onreconnected(async () => {
      this.connectionStateSig.set(HubConnectionState.Connected);
      try {
        await this.refreshLists();
        await conn.invoke('SubscribeOpen');
      } catch {
        // ignore: next push will resync, REST stays authoritative
      }
    });
    conn.onclose(() => this.connectionStateSig.set(HubConnectionState.Disconnected));

    return conn;
  }

  private onGameCreated(summary: GameSummary): void {
    this.upsertOpen(summary);
  }

  private onGameUpdated(summary: GameSummary): void {
    if (summary.status === 'Open') {
      this.upsertOpen(summary);
    } else if (summary.status === 'Running') {
      const wasOpen = this.openGamesSig().some((g) => g.id === summary.id);
      this.removeOpen(summary.id);
      this.upsertRunning(summary);
      // A gameUpdated(status=Running) push covers the same transition as
      // gameStarted; surface it so creators waiting in the lobby route in.
      if (wasOpen) {
        this.lastStartedGameIdSig.set(summary.id);
      }
    } else {
      this.removeOpen(summary.id);
      this.removeRunning(summary.id);
    }
  }

  private onGameStarted(gameId: string): void {
    this.removeOpen(gameId);
    this.lastStartedGameIdSig.set(gameId);
  }

  private onGameEnded(gameId: string): void {
    // The same event fires for natural finishes (Running → Finished) and
    // for the OpenLobbyJanitor's abandon path on unfilled Open games.
    // Wipe both lists; the LobbyComponent decides what to surface to
    // the user based on whether the id matches its pending game.
    this.removeOpen(gameId);
    this.removeRunning(gameId);
    this.lastEndedGameIdSig.set(gameId);
  }

  private onChatMessage(message: LobbyChatMessage): void {
    this.chatLogSig.update((log) => {
      const next = [...log, message];
      return next.length > CHAT_BACKLOG_MAX ? next.slice(next.length - CHAT_BACKLOG_MAX) : next;
    });
  }

  private upsertOpen(summary: GameSummary): void {
    this.openGamesSig.update((list) => upsertById(list, summary));
  }

  private upsertRunning(summary: GameSummary): void {
    this.runningGamesSig.update((list) => upsertById(list, summary));
  }

  private removeOpen(id: string): void {
    this.openGamesSig.update((list) => list.filter((g) => g.id !== id));
  }

  private removeRunning(id: string): void {
    this.runningGamesSig.update((list) => list.filter((g) => g.id !== id));
  }
}

function upsertById(list: readonly GameSummary[], summary: GameSummary): GameSummary[] {
  const idx = list.findIndex((g) => g.id === summary.id);
  if (idx === -1) {
    return [summary, ...list];
  }
  const next = list.slice();
  next[idx] = summary;
  return next;
}
