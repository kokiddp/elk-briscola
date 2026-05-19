import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { ErrorToastService } from '../../core/error-toast.service';
import { GameTablePageComponent } from './game-table-page.component';
import {
  Card,
  GameChatMessage,
  GameFinishedEvent,
  InvalidMoveCode,
  RedactedStateForUser,
} from './game.models';
import { GameService, SeatDisconnect } from './game.service';

/**
 * Unambiguous 2p snapshot: my hand has 3 cards, the only seat whose count
 * matches is seat 0 (the other seat has 2). The page latches mySeat = 0.
 */
const SNAPSHOT_2P: RedactedStateForUser = {
  gameId: 'g1',
  mode: 'TwoPlayer',
  phase: 'Playing',
  dealerSeat: 0,
  leaderSeat: 0,
  nextToPlaySeat: 0,
  trickNumber: 1,
  briscolaCard: { suit: 'Denari', rank: 'Re' },
  briscolaSuit: 'Denari',
  stockCount: 30,
  handCountsBySeat: [3, 2],
  myHand: [
    { suit: 'Bastoni', rank: 'Asso' },
    { suit: 'Coppe', rank: 'Tre' },
    { suit: 'Spade', rank: 'Re' },
  ],
  myPozzo: null,
  currentTrick: [],
  seatScores: [10, 5],
  outcome: null,
};

/**
 * Unambiguous 4p snapshot: my hand has 3 cards, only seat 0 has 3 cards;
 * latches mySeat = 0 → partner is seat 2.
 */
const SNAPSHOT_4P: RedactedStateForUser = {
  ...SNAPSHOT_2P,
  mode: 'FourPlayerTeams',
  handCountsBySeat: [3, 2, 2, 2],
  seatScores: [10, 20, 30, 40],
};

function cardSetsStub(): CardSetService {
  return {
    activeSet: () => ({
      id: 'p',
      name: 'p',
      resolveFront: (c: Card) => `/p/${c.suit}-${c.rank}.svg`,
      resolveBack: () => '/p/back.svg',
    }),
    activeSetId: () => 'p',
    fallbackFrontUrl: (c: Card) => `/p/${c.suit}-${c.rank}.svg`,
    fallbackBackUrl: () => '/p/back.svg',
  } as unknown as CardSetService;
}

interface MockGame {
  svc: GameService;
  setState(state: RedactedStateForUser | null): void;
  setInvalidMove(code: InvalidMoveCode | null): void;
  connect: ReturnType<typeof vi.fn>;
  connectAsSpectator: ReturnType<typeof vi.fn>;
  disconnect: ReturnType<typeof vi.fn>;
  play: ReturnType<typeof vi.fn>;
  sendChat: ReturnType<typeof vi.fn>;
  clearInvalidMove: ReturnType<typeof vi.fn>;
}

function makeGame(opts: {
  state?: RedactedStateForUser | null;
  chatLog?: GameChatMessage[];
  disconnects?: SeatDisconnect[];
  finished?: GameFinishedEvent | null;
  invalidMove?: InvalidMoveCode | null;
  spectator?: boolean;
  connect?: (id: string) => Promise<void>;
  connectAsSpectator?: (id: string) => Promise<void>;
  play?: (c: Card) => Promise<void>;
  sendChat?: (text: string) => Promise<void>;
}): MockGame {
  const stateSig = signal<RedactedStateForUser | null>(opts.state ?? null);
  const chatSig = signal<readonly GameChatMessage[]>(opts.chatLog ?? []);
  const disconnectsSig = signal<readonly SeatDisconnect[]>(opts.disconnects ?? []);
  const finishedSig = signal<GameFinishedEvent | null>(opts.finished ?? null);
  const invalidMoveSig = signal<InvalidMoveCode | null>(opts.invalidMove ?? null);
  const spectatorSig = signal(opts.spectator ?? false);

  const connect = vi.fn(opts.connect ?? (() => Promise.resolve()));
  const connectAsSpectator = vi.fn(
    opts.connectAsSpectator ??
      (() => {
        spectatorSig.set(true);
        return Promise.resolve();
      }),
  );
  const disconnect = vi.fn(() => Promise.resolve());
  const play = vi.fn(opts.play ?? (() => Promise.resolve()));
  const sendChat = vi.fn(opts.sendChat ?? (() => Promise.resolve()));
  const clearInvalidMove = vi.fn(() => invalidMoveSig.set(null));

  const earliest = (opts.disconnects ?? [])[0]?.deadline ?? null;

  const svc = {
    state: () => stateSig(),
    chatLog: () => chatSig(),
    disconnects: () => disconnectsSig(),
    idleWarnings: () => [] as readonly SeatDisconnect[],
    disconnectDeadline: () => earliest,
    lastFinished: () => finishedSig(),
    lastInvalidMove: () => invalidMoveSig(),
    isSpectator: () => spectatorSig(),
    legalMoves: () => new Set((stateSig()?.myHand ?? []).map((c) => `${c.suit}:${c.rank}`)),
    connect,
    connectAsSpectator,
    disconnect,
    play,
    sendChat,
    clearInvalidMove,
  } as unknown as GameService;
  return {
    svc,
    setState: (s) => stateSig.set(s),
    setInvalidMove: (code) => invalidMoveSig.set(code),
    connect,
    connectAsSpectator,
    disconnect,
    play,
    sendChat,
    clearInvalidMove,
  };
}

async function setup(
  opts: {
    state?: RedactedStateForUser | null;
    chatLog?: GameChatMessage[];
    disconnects?: SeatDisconnect[];
    finished?: GameFinishedEvent | null;
    invalidMove?: InvalidMoveCode | null;
    id?: string | null;
    spectator?: boolean;
    connect?: (id: string) => Promise<void>;
    connectAsSpectator?: (id: string) => Promise<void>;
    play?: (c: Card) => Promise<void>;
    sendChat?: (text: string) => Promise<void>;
  } = {},
) {
  const game = makeGame(opts);
  const toastError = vi.fn();
  const toast = { error: toastError } as unknown as ErrorToastService;
  const r = await render(GameTablePageComponent, {
    providers: [
      provideNoopAnimations(),
      provideRouter([]),
      { provide: GameService, useValue: game.svc },
      { provide: CardSetService, useValue: cardSetsStub() },
      { provide: ErrorToastService, useValue: toast },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            paramMap: convertToParamMap(opts.id === null ? {} : { id: opts.id ?? 'g1' }),
            data: { spectator: opts.spectator === true },
          },
        },
      },
    ],
  });
  const router = r.fixture.debugElement.injector.get(Router);
  return { ...r, ...game, toastError, router };
}

describe('GameTablePageComponent — rendering', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the connecting placeholder until a snapshot arrives', async () => {
    await setup({ state: null });
    expect(screen.getByTestId('connecting')).toBeInTheDocument();
    expect(screen.queryByTestId('my-hand-zone')).toBeNull();
  });

  it('renders the 2p layout with one opponent slot', async () => {
    await setup({ state: SNAPSHOT_2P });
    expect(screen.queryByTestId('connecting')).toBeNull();
    expect(screen.getByTestId('my-hand-zone')).toBeInTheDocument();
    expect(screen.getByTestId('briscola-zone')).toBeInTheDocument();
    expect(screen.getByTestId('trick-zone')).toBeInTheDocument();
    expect(screen.getByTestId('chat-zone')).toBeInTheDocument();
    expect(screen.getAllByTestId('opponent-slot')).toHaveLength(1);
  });

  it('renders three opponent slots in the 4p layout', async () => {
    await setup({ state: SNAPSHOT_4P });
    expect(screen.getAllByTestId('opponent-slot')).toHaveLength(3);
  });

  it('renders my hand cards as buttons (one per card)', async () => {
    await setup({ state: SNAPSHOT_2P });
    expect(screen.getAllByTestId('hand-card')).toHaveLength(3);
  });

  it('renders the scoreboard with seat scores', async () => {
    await setup({ state: SNAPSHOT_2P });
    expect(screen.getByTestId('scoreboard')).toBeInTheDocument();
    const values = screen.getAllByTestId('score-value').map((el) => el.textContent?.trim());
    expect(values).toEqual(['10', '5']);
  });

  it('shows the reconnect banner when a disconnect deadline is active', async () => {
    await setup({
      state: SNAPSHOT_2P,
      disconnects: [{ seatIndex: 1, deadline: new Date(Date.now() + 30_000) }],
    });
    expect(screen.getByTestId('reconnect-banner')).toBeInTheDocument();
  });

  it('shows the end-game dialog when a finished event has landed', async () => {
    await setup({
      state: { ...SNAPSHOT_2P, phase: 'Finished' },
      finished: {
        outcome: { kind: 'Winner', winnerKey: 0 },
        seatScores: [70, 50],
        reason: 'Normal',
      },
    });
    expect(screen.getByTestId('end-game-dialog')).toBeInTheDocument();
  });
});

describe('GameTablePageComponent — lifecycle', () => {
  it('calls GameService.connect with the :id route param on init', async () => {
    const { connect, connectAsSpectator } = await setup({ id: 'game-42' });
    expect(connect).toHaveBeenCalledWith('game-42');
    expect(connectAsSpectator).not.toHaveBeenCalled();
  });

  it('does not call connect when the :id route param is missing', async () => {
    const { connect } = await setup({ id: null });
    expect(connect).not.toHaveBeenCalled();
  });

  it('calls GameService.disconnect on destroy', async () => {
    const { disconnect, fixture } = await setup();
    fixture.destroy();
    expect(disconnect).toHaveBeenCalledTimes(1);
  });

  it('calls connectAsSpectator when route data marks the page as spectator', async () => {
    const { connect, connectAsSpectator } = await setup({ id: 'game-42', spectator: true });
    expect(connectAsSpectator).toHaveBeenCalledWith('game-42');
    expect(connect).not.toHaveBeenCalled();
  });
});

describe('GameTablePageComponent — MyHand visibility rule', () => {
  it('hides MyHand when the snapshot has a null myHand (server redaction)', async () => {
    // Server has redacted our hand even though we are not in spectator mode
    // (e.g. snapshot replayed to a non-participant). The component MUST NOT
    // render MyHand on this signal alone.
    const redacted: RedactedStateForUser = { ...SNAPSHOT_2P, myHand: null };
    await setup({ state: redacted, spectator: false });
    expect(screen.queryByTestId('my-hand-zone')).toBeNull();
    expect(screen.queryAllByTestId('hand-card')).toHaveLength(0);
  });

  it('renders MyHand even with an empty hand array (just played the last card)', async () => {
    // Empty hand is a normal late-game state — different from null which
    // signals server-side redaction. The component should still show the
    // (empty) hand zone so the layout doesn't reflow.
    const empty: RedactedStateForUser = { ...SNAPSHOT_2P, myHand: [] };
    await setup({ state: empty, spectator: false });
    expect(screen.getByTestId('my-hand-zone')).toBeInTheDocument();
    expect(screen.queryAllByTestId('hand-card')).toHaveLength(0);
  });

  it('keeps MyHand hidden even when the snapshot carries a hand if the page is in spectator mode', async () => {
    // Defense in depth: the route says spectator, so even a misbehaving
    // server snapshot with our hand attached must NOT render the cards.
    await setup({ state: SNAPSHOT_2P, spectator: true });
    expect(screen.queryByTestId('my-hand-zone')).toBeNull();
    expect(screen.getByTestId('spectator-banner')).toBeInTheDocument();
  });
});

describe('GameTablePageComponent — spectator mode', () => {
  it('hides MyHand and shows the spectator banner when isSpectator is true', async () => {
    await setup({ state: SNAPSHOT_2P, spectator: true });
    expect(screen.queryByTestId('my-hand-zone')).toBeNull();
    expect(screen.queryAllByTestId('hand-card')).toHaveLength(0);
    expect(screen.getByTestId('spectator-banner')).toBeInTheDocument();
  });

  it('renders MyHand normally when isSpectator is false', async () => {
    await setup({ state: SNAPSHOT_2P, spectator: false });
    expect(screen.getByTestId('my-hand-zone')).toBeInTheDocument();
    expect(screen.queryByTestId('spectator-banner')).toBeNull();
  });

  it('renders both 2p players as opponent slots for spectators (left + right)', async () => {
    await setup({ state: SNAPSHOT_2P, spectator: true });
    expect(screen.getByTestId('briscola-zone')).toBeInTheDocument();
    expect(screen.getByTestId('trick-zone')).toBeInTheDocument();
    expect(screen.getByTestId('scoreboard-zone')).toBeInTheDocument();
    const slots = screen.getAllByTestId('opponent-slot');
    expect(slots).toHaveLength(2);
    expect(slots[0]?.classList.contains('opponent-left')).toBe(true);
    expect(slots[1]?.classList.contains('opponent-right')).toBe(true);
  });
});

describe('GameTablePageComponent — mySeat latching', () => {
  let warn: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => warn.mockRestore());

  it('does not latch when every seat shares my hand size', async () => {
    const ambiguous: RedactedStateForUser = {
      ...SNAPSHOT_2P,
      handCountsBySeat: [3, 3],
    };
    await setup({ state: ambiguous });
    // No latch → no opponent slot rendered yet.
    expect(screen.queryAllByTestId('opponent-slot')).toHaveLength(0);
  });

  it('latches once a snapshot is unambiguous and keeps mySeat sticky as counts drift', async () => {
    const initial: RedactedStateForUser = {
      ...SNAPSHOT_2P,
      handCountsBySeat: [3, 2], // unambiguous: mySeat = 0
    };
    const { fixture, setState } = await setup({ state: initial });
    expect(screen.getAllByTestId('opponent-slot')).toHaveLength(1);

    // Mid-game both seats happen to share the count again. mySeat must
    // stay 0 — without latching, the heuristic would re-pick.
    setState({ ...initial, handCountsBySeat: [2, 2], myHand: initial.myHand?.slice(0, 2) ?? null });
    fixture.detectChanges();
    expect(screen.getAllByTestId('opponent-slot')).toHaveLength(1);
  });
});

describe('GameTablePageComponent — opponentSlots rotation (4p)', () => {
  function snapshotWithMyHand(handLen: number, meSeat: number): RedactedStateForUser {
    const counts = [2, 2, 2, 2];
    counts[meSeat] = handLen;
    return {
      ...SNAPSHOT_4P,
      handCountsBySeat: counts,
      myHand: Array.from({ length: handLen }, (_, i) => ({
        suit: 'Bastoni' as const,
        rank: ['Asso', 'Tre', 'Re'][i % 3] as 'Asso' | 'Tre' | 'Re',
      })),
    };
  }

  it('places the partner at the middle slot for each possible mySeat', async () => {
    // Render four pages, one per possible mySeat. The opponent at the middle
    // slot (index 1) must be the seat across from me (me + 2) mod 4.
    for (const me of [0, 1, 2, 3]) {
      const partner = (me + 2) % 4;
      TestBed.resetTestingModule();
      const r = await setup({ state: snapshotWithMyHand(3, me) });
      const slots = screen.getAllByTestId('opponent-slot');
      expect(slots).toHaveLength(3);
      const middleChip = slots[1]?.querySelector('[data-testid="partner-chip"]');
      expect(
        middleChip,
        `partner chip at middle slot for mySeat=${me}, partner=${partner}`,
      ).not.toBeNull();
      r.fixture.destroy();
    }
  });
});

describe('GameTablePageComponent — invalidMove surfacing', () => {
  it('shows a toast for each invalidMove code and clears the signal afterwards', async () => {
    const { setInvalidMove, toastError, clearInvalidMove, fixture } = await setup({
      state: SNAPSHOT_2P,
    });
    expect(toastError).not.toHaveBeenCalled();

    setInvalidMove('NotYourTurn');
    fixture.detectChanges();
    expect(toastError).toHaveBeenCalledTimes(1);
    expect(toastError.mock.calls[0]?.[0]).toMatch(/your turn/i);
    expect(clearInvalidMove).toHaveBeenCalled();

    // Same code can fire again later (because clearInvalidMove() reset it).
    setInvalidMove('RateLimited');
    fixture.detectChanges();
    expect(toastError).toHaveBeenCalledTimes(2);
    expect(toastError.mock.calls[1]?.[0]).toMatch(/too fast|moment/i);
  });
});
