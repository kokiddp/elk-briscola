import { signal } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { GameTablePageComponent } from './game-table-page.component';
import { Card, GameChatMessage, GameFinishedEvent, RedactedStateForUser } from './game.models';
import { GameService, SeatDisconnect } from './game.service';

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
  // Seat 0 (me) holds the visible hand of 3; opponent has 3 too — the
  // tie-breaker picks the first match.
  handCountsBySeat: [3, 3],
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

const SNAPSHOT_4P: RedactedStateForUser = {
  ...SNAPSHOT_2P,
  mode: 'FourPlayerTeams',
  handCountsBySeat: [3, 3, 3, 3],
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

function makeGame(opts: {
  state?: RedactedStateForUser | null;
  chatLog?: GameChatMessage[];
  disconnects?: SeatDisconnect[];
  finished?: GameFinishedEvent | null;
  connect?: (id: string) => Promise<void>;
  play?: (c: Card) => Promise<void>;
  sendChat?: (text: string) => Promise<void>;
}): {
  svc: GameService;
  connect: ReturnType<typeof vi.fn>;
  disconnect: ReturnType<typeof vi.fn>;
  play: ReturnType<typeof vi.fn>;
  sendChat: ReturnType<typeof vi.fn>;
} {
  const stateSig = signal<RedactedStateForUser | null>(opts.state ?? null);
  const chatSig = signal<readonly GameChatMessage[]>(opts.chatLog ?? []);
  const disconnectsSig = signal<readonly SeatDisconnect[]>(opts.disconnects ?? []);
  const finishedSig = signal<GameFinishedEvent | null>(opts.finished ?? null);

  const connect = vi.fn(opts.connect ?? (() => Promise.resolve()));
  const disconnect = vi.fn(() => Promise.resolve());
  const play = vi.fn(opts.play ?? (() => Promise.resolve()));
  const sendChat = vi.fn(opts.sendChat ?? (() => Promise.resolve()));

  const earliest = (opts.disconnects ?? [])[0]?.deadline ?? null;

  const svc = {
    state: () => stateSig(),
    chatLog: () => chatSig(),
    disconnects: () => disconnectsSig(),
    disconnectDeadline: () => earliest,
    lastFinished: () => finishedSig(),
    legalMoves: () => new Set((stateSig()?.myHand ?? []).map((c) => `${c.suit}:${c.rank}`)),
    connect,
    disconnect,
    play,
    sendChat,
  } as unknown as GameService;
  return { svc, connect, disconnect, play, sendChat };
}

async function setup(
  opts: {
    state?: RedactedStateForUser | null;
    chatLog?: GameChatMessage[];
    disconnects?: SeatDisconnect[];
    finished?: GameFinishedEvent | null;
    id?: string | null;
    connect?: (id: string) => Promise<void>;
    play?: (c: Card) => Promise<void>;
    sendChat?: (text: string) => Promise<void>;
  } = {},
) {
  const game = makeGame(opts);
  const r = await render(GameTablePageComponent, {
    providers: [
      provideNoopAnimations(),
      provideRouter([]),
      { provide: GameService, useValue: game.svc },
      { provide: CardSetService, useValue: cardSetsStub() },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            paramMap: convertToParamMap(opts.id === null ? {} : { id: opts.id ?? 'g1' }),
          },
        },
      },
    ],
  });
  const router = r.fixture.debugElement.injector.get(Router);
  return { ...r, ...game, router };
}

describe('GameTablePageComponent', () => {
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

  it('calls GameService.connect with the :id route param on init', async () => {
    const { connect } = await setup({ id: 'game-42' });
    expect(connect).toHaveBeenCalledWith('game-42');
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
});
