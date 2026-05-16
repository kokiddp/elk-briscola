import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from '../../core/auth.service';
import { Card, GameChatMessage, GameFinishedEvent, RedactedStateForUser } from './game.models';
import { GameService } from './game.service';

function authStub(): AuthService {
  return {
    getAccessTokenAsync: () => Promise.resolve('t-1'),
    currentUser: () => ({
      id: 'u1',
      username: 'alice',
      displayName: 'Alice',
      email: 'a@a',
      activeCardSetId: 'placeholder',
      ranking: { elo: 1000, wins: 0, losses: 0, draws: 0, gamesPlayed: 0, updatedAt: '' },
    }),
  } as unknown as AuthService;
}

function makeService(): GameService {
  TestBed.configureTestingModule({
    providers: [{ provide: AuthService, useValue: authStub() }, GameService],
  });
  return TestBed.inject(GameService);
}

const ASSO_BASTONI: Card = { suit: 'Bastoni', rank: 'Asso' };
const TRE_COPPE: Card = { suit: 'Coppe', rank: 'Tre' };
const RE_DENARI: Card = { suit: 'Denari', rank: 'Re' };

function makeSnapshot(opts: Partial<RedactedStateForUser> = {}): RedactedStateForUser {
  return {
    gameId: 'g1',
    mode: 'TwoPlayer',
    phase: 'Playing',
    dealerSeat: 0,
    leaderSeat: 0,
    nextToPlaySeat: 0,
    trickNumber: 1,
    briscolaCard: RE_DENARI,
    briscolaSuit: 'Denari',
    stockCount: 30,
    handCountsBySeat: [3, 3],
    myHand: [ASSO_BASTONI, TRE_COPPE],
    myPozzo: null,
    currentTrick: [],
    seatScores: [0, 0],
    outcome: null,
    ...opts,
  };
}

// Internal accessor cast to drive the private event reducers without
// spinning up a real SignalR connection.
function reducers(svc: GameService) {
  return svc as unknown as {
    applySnapshot(s: RedactedStateForUser): void;
    upsertDisconnect(seat: number, iso: string): void;
    removeDisconnect(seat: number): void;
    upsertIdleWarning(seat: number, iso: string): void;
    appendChat(m: GameChatMessage): void;
    stateSig: {
      update: (fn: (s: RedactedStateForUser | null) => RedactedStateForUser | null) => void;
    };
    lastInvalidMoveSig: { set: (code: string) => void };
    lastFinishedSig: { set: (e: GameFinishedEvent) => void };
  };
}

describe('GameService.applySnapshot', () => {
  let svc: GameService;
  beforeEach(() => {
    svc = makeService();
  });

  it('exposes the snapshot via the state signal', () => {
    reducers(svc).applySnapshot(makeSnapshot());
    expect(svc.state()?.gameId).toBe('g1');
    expect(svc.myHand()).toEqual([ASSO_BASTONI, TRE_COPPE]);
  });

  it('forces phase to Finished when the snapshot carries an outcome', () => {
    reducers(svc).applySnapshot(
      makeSnapshot({
        phase: 'Playing',
        outcome: { kind: 'Winner', winnerKey: 0 },
      }),
    );
    expect(svc.state()?.phase).toBe('Finished');
  });
});

describe('GameService.legalMoves', () => {
  let svc: GameService;
  beforeEach(() => {
    svc = makeService();
  });

  it('is empty when no state has been applied', () => {
    expect(svc.legalMoves().size).toBe(0);
  });

  it('contains all hand cards while Playing', () => {
    reducers(svc).applySnapshot(makeSnapshot());
    const moves = svc.legalMoves();
    expect(moves.has('Bastoni:Asso')).toBe(true);
    expect(moves.has('Coppe:Tre')).toBe(true);
    expect(moves.size).toBe(2);
  });

  it('is empty during the Dealing phase', () => {
    reducers(svc).applySnapshot(makeSnapshot({ phase: 'Dealing' }));
    expect(svc.legalMoves().size).toBe(0);
  });

  it('is empty when the hand is empty or null', () => {
    reducers(svc).applySnapshot(makeSnapshot({ myHand: [] }));
    expect(svc.legalMoves().size).toBe(0);
    reducers(svc).applySnapshot(makeSnapshot({ myHand: null }));
    expect(svc.legalMoves().size).toBe(0);
  });
});

describe('GameService disconnect/idle reducers', () => {
  let svc: GameService;
  beforeEach(() => {
    svc = makeService();
  });

  it('upserts and removes player disconnects', () => {
    reducers(svc).upsertDisconnect(1, '2026-05-17T10:00:00Z');
    expect(svc.disconnects()).toHaveLength(1);
    expect(svc.disconnects()[0]?.seatIndex).toBe(1);

    // Newer deadline replaces the entry in place.
    reducers(svc).upsertDisconnect(1, '2026-05-17T10:02:00Z');
    expect(svc.disconnects()).toHaveLength(1);
    expect(svc.disconnects()[0]?.deadline.toISOString()).toBe('2026-05-17T10:02:00.000Z');

    reducers(svc).removeDisconnect(1);
    expect(svc.disconnects()).toHaveLength(0);
  });

  it('ignores invalid ISO deadlines', () => {
    reducers(svc).upsertDisconnect(1, 'not-a-date');
    expect(svc.disconnects()).toHaveLength(0);
  });

  it('exposes the earliest disconnect deadline via disconnectDeadline', () => {
    reducers(svc).upsertDisconnect(0, '2026-05-17T10:05:00Z');
    reducers(svc).upsertDisconnect(1, '2026-05-17T10:01:00Z');
    expect(svc.disconnectDeadline()?.toISOString()).toBe('2026-05-17T10:01:00.000Z');
  });

  it('returns null disconnect deadline when nobody is disconnected', () => {
    expect(svc.disconnectDeadline()).toBeNull();
  });

  it('tracks idle warnings the same way', () => {
    reducers(svc).upsertIdleWarning(0, '2026-05-17T10:00:00Z');
    expect(svc.idleWarnings()).toHaveLength(1);
    reducers(svc).upsertIdleWarning(0, '2026-05-17T10:02:00Z');
    expect(svc.idleWarnings()[0]?.deadline.toISOString()).toBe('2026-05-17T10:02:00.000Z');
  });
});

describe('GameService chat', () => {
  it('appends chat messages and caps the backlog', () => {
    const svc = makeService();
    for (let i = 0; i < 600; i++) {
      reducers(svc).appendChat({
        id: `m-${i}`,
        gameId: 'g1',
        fromUserId: 'u1',
        fromUserName: 'alice',
        text: `m-${i}`,
        createdAt: '2026-05-17T10:00:00Z',
      });
    }
    expect(svc.chatLog()).toHaveLength(500);
    expect(svc.chatLog()[0]?.id).toBe('m-100');
    expect(svc.chatLog()[svc.chatLog().length - 1]?.id).toBe('m-599');
  });
});

describe('GameService invalid-move signal', () => {
  it('exposes and clears the last invalid-move code', () => {
    const svc = makeService();
    reducers(svc).lastInvalidMoveSig.set('CardNotInHand');
    expect(svc.lastInvalidMove()).toBe('CardNotInHand');
    svc.clearInvalidMove();
    expect(svc.lastInvalidMove()).toBeNull();
  });
});
