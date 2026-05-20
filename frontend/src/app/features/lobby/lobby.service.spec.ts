import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth.service';
import { GameSummary } from './lobby.models';
import { LobbyService } from './lobby.service';

const OPEN_GAME: GameSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  mode: 'TwoPlayer',
  name: 'first open',
  status: 'Open',
  occupiedSeats: 1,
  totalSeats: 2,
  isPrivate: false,
  createdAt: '2026-05-16T10:00:00Z',
  startedAt: null,
};

const RUNNING_GAME: GameSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  mode: 'FourPlayerTeams',
  name: 'in progress',
  status: 'Running',
  occupiedSeats: 4,
  totalSeats: 4,
  isPrivate: false,
  createdAt: '2026-05-16T09:55:00Z',
  startedAt: '2026-05-16T09:57:00Z',
};

function authStub(): AuthService {
  return {
    getAccessTokenAsync: () => Promise.resolve('test-token'),
  } as unknown as AuthService;
}

async function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: authStub() },
      LobbyService,
    ],
  });
  const service = TestBed.inject(LobbyService);
  const ctrl = TestBed.inject(HttpTestingController);
  return { service, ctrl };
}

describe('LobbyService', () => {
  let svc: LobbyService;
  let ctrl: HttpTestingController;

  beforeEach(async () => {
    const s = await setup();
    svc = s.service;
    ctrl = s.ctrl;
  });

  it('refreshLists hydrates open and running signals from REST', async () => {
    const promise = svc.refreshLists();

    const openReq = ctrl.expectOne(
      (r) => r.url === '/api/v1/games' && r.params.get('status') === 'Open',
    );
    const runReq = ctrl.expectOne(
      (r) => r.url === '/api/v1/games' && r.params.get('status') === 'Running',
    );
    openReq.flush([OPEN_GAME]);
    runReq.flush([RUNNING_GAME]);
    await promise;

    expect(svc.openGames()).toEqual([OPEN_GAME]);
    expect(svc.runningGames()).toEqual([RUNNING_GAME]);
    ctrl.verify();
  });

  it('createGame POSTs the request body and returns the created detail', async () => {
    const promise = svc.createGame({
      mode: 'TwoPlayer',
      isPrivate: false,
      password: null,
    });
    const req = ctrl.expectOne('/api/v1/games');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({ mode: 'TwoPlayer', isPrivate: false });
    expect(req.request.body).not.toHaveProperty('name');
    req.flush({
      id: 'abc',
      mode: 'TwoPlayer',
      status: 'Open',
      isPrivate: false,
      createdAt: '2026-05-16T10:00:00Z',
      startedAt: null,
      endedAt: null,
      seats: [null, null],
    });
    const detail = await promise;
    expect(detail.id).toBe('abc');
    ctrl.verify();
  });

  it('joinGame POSTs with the password payload', async () => {
    const promise = svc.joinGame('abc', 'secret');
    const req = ctrl.expectOne('/api/v1/games/abc/join');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ password: 'secret' });
    req.flush({
      id: 'abc',
      mode: 'TwoPlayer',
      name: 'hello',
      status: 'Open',
      isPrivate: true,
      createdAt: '2026-05-16T10:00:00Z',
      startedAt: null,
      endedAt: null,
      seats: ['u1', null],
    });
    const detail = await promise;
    expect(detail.id).toBe('abc');
    ctrl.verify();
  });

  it('leaveGame POSTs to the leave endpoint', async () => {
    const promise = svc.leaveGame('abc');
    const req = ctrl.expectOne('/api/v1/games/abc/leave');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await promise;
    ctrl.verify();
  });
});

describe('LobbyService event reducers', () => {
  let svc: LobbyService;

  beforeEach(async () => {
    const s = await setup();
    svc = s.service;
  });

  function applyCreated(summary: GameSummary) {
    (svc as unknown as { onGameCreated(s: GameSummary): void }).onGameCreated(summary);
  }
  function applyUpdated(summary: GameSummary) {
    (svc as unknown as { onGameUpdated(s: GameSummary): void }).onGameUpdated(summary);
  }
  function applyStarted(id: string) {
    (svc as unknown as { onGameStarted(id: string): void }).onGameStarted(id);
  }
  function applyEnded(id: string) {
    (svc as unknown as { onGameEnded(id: string): void }).onGameEnded(id);
  }
  function applyChat(msg: {
    id: string;
    fromUserId: string;
    fromUserName: string;
    text: string;
    createdAt: string;
  }) {
    (
      svc as unknown as {
        onChatMessage(m: typeof msg): void;
      }
    ).onChatMessage(msg);
  }

  it('gameCreated prepends the summary to openGames', () => {
    applyCreated(OPEN_GAME);
    expect(svc.openGames()).toEqual([OPEN_GAME]);
  });

  it('gameUpdated to Open upserts and replaces existing entry', () => {
    applyCreated(OPEN_GAME);
    const updated = { ...OPEN_GAME, occupiedSeats: 2 };
    applyUpdated(updated);
    expect(svc.openGames()).toEqual([updated]);
  });

  it('gameUpdated to Running moves the entry from open to running', () => {
    applyCreated(OPEN_GAME);
    const moved = { ...OPEN_GAME, status: 'Running' as const, startedAt: '2026-05-16T10:01:00Z' };
    applyUpdated(moved);
    expect(svc.openGames()).toEqual([]);
    expect(svc.runningGames()).toEqual([moved]);
  });

  it('gameStarted drops the game from openGames', () => {
    applyCreated(OPEN_GAME);
    applyStarted(OPEN_GAME.id);
    expect(svc.openGames()).toEqual([]);
  });

  it('gameEnded drops the game from runningGames', () => {
    applyUpdated(RUNNING_GAME);
    applyEnded(RUNNING_GAME.id);
    expect(svc.runningGames()).toEqual([]);
  });

  it('chatMessage appends to the chat log', () => {
    applyChat({
      id: 'm1',
      fromUserId: 'u1',
      fromUserName: 'alice',
      text: 'hi',
      createdAt: '2026-05-16T10:00:00Z',
    });
    expect(svc.chatLog()).toHaveLength(1);
    expect(svc.chatLog()[0]?.text).toBe('hi');
  });
});

describe('LobbyService auto-route gate (H4)', () => {
  function setupWith(url: string) {
    const navigateByUrl = vi.fn(() => Promise.resolve(true));
    const routerStub = {
      get url() {
        return url;
      },
      navigateByUrl,
    } as unknown as Router;

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            getAccessTokenAsync: () => Promise.resolve('test-token'),
            currentUser: () => ({ id: 'me' }),
          } as unknown as AuthService,
        },
        { provide: Router, useValue: routerStub },
        LobbyService,
      ],
    });
    const svc = TestBed.inject(LobbyService);
    return { svc, navigateByUrl };
  }

  function seatMeIn(svc: LobbyService, gameId: string) {
    // Inject an open game with the current user seated so pendingGameId
    // captures, then drive the lastStartedGameIdSig transition by hand.
    (svc as unknown as { onGameCreated(s: GameSummary): void }).onGameCreated({
      id: gameId,
      mode: 'TwoPlayer',
      name: 'pending',
      status: 'Open',
      occupiedSeats: 1,
      totalSeats: 2,
      isPrivate: false,
      createdAt: '2026-05-20T12:00:00Z',
      startedAt: null,
      seatPlayers: [{ userId: 'me', displayName: 'me', elo: 1500 }, null],
    } as GameSummary);
    TestBed.flushEffects();
  }

  function fireStarted(svc: LobbyService, gameId: string) {
    (svc as unknown as { onGameStarted(id: string): void }).onGameStarted(gameId);
    TestBed.flushEffects();
  }

  it('navigates into /game/:id from /lobby when the pending game starts', () => {
    const { svc, navigateByUrl } = setupWith('/lobby');
    const gid = '00000000-0000-0000-0000-00000000aaaa';
    seatMeIn(svc, gid);
    fireStarted(svc, gid);
    expect(navigateByUrl).toHaveBeenCalledWith(`/game/${gid}`);
  });

  it('navigates into /game/:id from /profile when the pending game starts', () => {
    const { svc, navigateByUrl } = setupWith('/profile');
    const gid = '00000000-0000-0000-0000-00000000bbbb';
    seatMeIn(svc, gid);
    fireStarted(svc, gid);
    expect(navigateByUrl).toHaveBeenCalledWith(`/game/${gid}`);
  });

  it('does NOT navigate when the user is already on a /game/* route', () => {
    // Spectator watching one game, separate pending game of theirs
    // fills — they should stay where they are.
    const { svc, navigateByUrl } = setupWith('/game/00000000-0000-0000-0000-000000000111');
    const gid = '00000000-0000-0000-0000-00000000cccc';
    seatMeIn(svc, gid);
    fireStarted(svc, gid);
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});
