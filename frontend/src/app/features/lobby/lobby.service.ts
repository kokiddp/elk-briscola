import { HttpClient, HttpParams } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
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

  private readonly openGamesSig = signal<readonly GameSummary[]>([]);
  private readonly runningGamesSig = signal<readonly GameSummary[]>([]);
  private readonly chatLogSig = signal<readonly LobbyChatMessage[]>([]);
  private readonly connectionStateSig = signal<HubConnectionState>(HubConnectionState.Disconnected);

  private connection: HubConnection | null = null;
  private connectPromise: Promise<void> | null = null;

  readonly openGames = computed(() => this.openGamesSig());
  readonly runningGames = computed(() => this.runningGamesSig());
  readonly chatLog = computed(() => this.chatLogSig());
  readonly connectionState = computed(() => this.connectionStateSig());

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
          this.connectionStateSig.set(this.connection.state);
        }
        await this.connection.invoke('SubscribeOpen');
      } catch (err) {
        this.connectionStateSig.set(this.connection?.state ?? HubConnectionState.Disconnected);
        throw err;
      } finally {
        this.connectPromise = null;
      }
    })();
    return this.connectPromise;
  }

  async disconnect(): Promise<void> {
    const conn = this.connection;
    this.connection = null;
    this.connectionStateSig.set(HubConnectionState.Disconnected);
    if (conn) {
      try {
        if (conn.state === HubConnectionState.Connected) {
          await conn.invoke('UnsubscribeOpen').catch(() => undefined);
        }
        await conn.stop();
      } catch {
        // ignore: best-effort teardown
      }
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
      this.removeOpen(summary.id);
      this.upsertRunning(summary);
    } else {
      this.removeOpen(summary.id);
      this.removeRunning(summary.id);
    }
  }

  private onGameStarted(gameId: string): void {
    this.removeOpen(gameId);
  }

  private onGameEnded(gameId: string): void {
    this.removeRunning(gameId);
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
