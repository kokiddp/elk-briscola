import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { LobbyComponent } from './lobby.component';
import { GameDetail, GameSummary } from './lobby.models';
import { LobbyService } from './lobby.service';

interface MockLobby {
  svc: LobbyService;
  setOpen(games: GameSummary[]): void;
  setRunning(games: GameSummary[]): void;
  joinGame: ReturnType<typeof vi.fn>;
  createGame: ReturnType<typeof vi.fn>;
}

function makeLobby(
  opts: {
    open?: GameSummary[];
    running?: GameSummary[];
    joinGame?: (id: string, password: string | null | undefined) => Promise<GameDetail>;
    createGame?: () => Promise<GameDetail>;
  } = {},
): MockLobby {
  const openSig = signal<readonly GameSummary[]>(opts.open ?? []);
  const runningSig = signal<readonly GameSummary[]>(opts.running ?? []);
  const chatLogSig = signal<readonly never[]>([]);
  const lastStartedSig = signal<string | null>(null);
  const lastEndedSig = signal<string | null>(null);
  const joinGame = vi.fn(
    opts.joinGame ?? ((id: string) => Promise.resolve({ id, status: 'Running' } as GameDetail)),
  );
  const createGame = vi.fn(
    opts.createGame ?? (() => Promise.resolve({ id: 'new-game', status: 'Running' } as GameDetail)),
  );
  const clearLastStartedGameId = vi.fn(() => lastStartedSig.set(null));
  const clearLastEndedGameId = vi.fn(() => lastEndedSig.set(null));
  const pendingSig = signal<GameSummary | null>(null);
  const svc = {
    openGames: () => openSig(),
    runningGames: () => runningSig(),
    chatLog: () => chatLogSig(),
    lastStartedGameId: () => lastStartedSig(),
    clearLastStartedGameId,
    lastEndedGameId: () => lastEndedSig(),
    clearLastEndedGameId,
    pendingGame: () => pendingSig(),
    pendingGameId: () => pendingSig()?.id ?? null,
    hasPendingGame: () => pendingSig() !== null,
    connect: () => Promise.resolve(),
    disconnect: () => Promise.resolve(),
    createGame,
    joinGame,
    leaveGame: vi.fn(() => Promise.resolve()),
    sendChat: () => Promise.resolve(),
  } as unknown as LobbyService;
  return {
    svc,
    setOpen: (games) => openSig.set(games),
    setRunning: (games) => runningSig.set(games),
    joinGame,
    createGame,
  };
}

async function setup(
  opts: {
    open?: GameSummary[];
    running?: GameSummary[];
    joinGame?: (id: string, password: string | null | undefined) => Promise<GameDetail>;
    createGame?: () => Promise<GameDetail>;
  } = {},
) {
  const lobby = makeLobby(opts);
  const r = await render(LobbyComponent, {
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: '**', component: LobbyComponent }]),
      { provide: LobbyService, useValue: lobby.svc },
    ],
  });
  const router = r.fixture.debugElement.injector.get(Router);
  return { ...r, ...lobby, router };
}

const OPEN_2P: GameSummary = {
  id: 'g1',
  mode: 'TwoPlayer',
  name: 'Casual match',
  status: 'Open',
  occupiedSeats: 1,
  totalSeats: 2,
  isPrivate: false,
  createdAt: '2026-05-16T10:00:00Z',
  startedAt: null,
};

const OPEN_PRIVATE: GameSummary = {
  id: 'g-priv',
  mode: 'TwoPlayer',
  name: 'Private duel',
  status: 'Open',
  occupiedSeats: 1,
  totalSeats: 2,
  isPrivate: true,
  createdAt: '2026-05-16T10:01:00Z',
  startedAt: null,
};

const RUNNING_4P: GameSummary = {
  id: 'g2',
  mode: 'FourPlayerTeams',
  name: 'Team rumble',
  status: 'Running',
  occupiedSeats: 4,
  totalSeats: 4,
  isPrivate: false,
  createdAt: '2026-05-16T09:55:00Z',
  startedAt: '2026-05-16T09:57:00Z',
};

describe('LobbyComponent rendering', () => {
  it('renders the title and create button', async () => {
    await setup();
    expect(screen.getByRole('heading', { name: /lobby/i, level: 1 })).toBeInTheDocument();
    expect(screen.getByTestId('create-game')).toBeInTheDocument();
  });

  it('shows the empty state when there are no open games', async () => {
    await setup();
    expect(screen.getByText(/no open games yet/i)).toBeInTheDocument();
  });

  it('lists open games with mode chip and seat counter', async () => {
    await setup({ open: [OPEN_2P] });
    expect(screen.getByTestId('open-list')).toBeInTheDocument();
    expect(screen.getByTestId('mode-chip')).toHaveTextContent(/2 players/i);
    expect(screen.getByTestId('seats')).toHaveTextContent('1 / 2');
    expect(screen.getByTestId('join-button')).toBeEnabled();
  });
});

describe('LobbyComponent filter logic', () => {
  it('renders Open and Running games into the two separate lists', async () => {
    await setup({ open: [OPEN_2P], running: [RUNNING_4P] });
    expect(screen.getByTestId('open-list')).toBeInTheDocument();
    expect(screen.getByTestId('running-list')).toBeInTheDocument();
    expect(screen.getAllByTestId('mode-chip')[0]).toHaveTextContent(/2 players/i);
    // Running list shows its own row.
    expect(screen.getByTestId('running-list').querySelectorAll('li').length).toBe(1);
  });

  it('updates the lists reactively when the service signals change', async () => {
    const { setOpen, setRunning, fixture } = await setup();
    expect(screen.queryByTestId('open-list')).toBeNull();

    setOpen([OPEN_2P]);
    fixture.detectChanges();
    expect(screen.getByTestId('open-list').querySelectorAll('li').length).toBe(1);

    setRunning([RUNNING_4P]);
    setOpen([]);
    fixture.detectChanges();
    expect(screen.queryByTestId('open-list')).toBeNull();
    expect(screen.getByTestId('running-list').querySelectorAll('li').length).toBe(1);
  });
});

describe('LobbyComponent join flow', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('navigates to /game/:id after a successful public join', async () => {
    const { joinGame, router, fixture } = await setup({ open: [OPEN_2P] });
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fireEvent.click(screen.getByTestId('join-button'));
    await fixture.whenStable();

    expect(joinGame).toHaveBeenCalledWith('g1', null);
    expect(nav).toHaveBeenCalledWith('/game/g1');
  });

  it('opens the join-password modal on a private game and joins with the entered value', async () => {
    const { joinGame, router, fixture } = await setup({ open: [OPEN_PRIVATE] });
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fireEvent.click(screen.getByTestId('join-button'));
    await fixture.whenStable();

    // The modal is mounted; window.prompt is NOT used.
    const dialog = screen.getByTestId('join-password-dialog');
    expect(dialog).toBeInTheDocument();
    const input = screen.getByTestId('join-password-input') as HTMLInputElement;
    fireEvent.input(input, { target: { value: 'letmein' } });
    fireEvent.click(screen.getByTestId('join-password-submit'));
    await fixture.whenStable();

    expect(joinGame).toHaveBeenCalledWith('g-priv', 'letmein');
    expect(nav).toHaveBeenCalledWith('/game/g-priv');
  });

  it('does not join when the password modal is cancelled', async () => {
    const { joinGame, router, fixture } = await setup({ open: [OPEN_PRIVATE] });
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fireEvent.click(screen.getByTestId('join-button'));
    await fixture.whenStable();
    fireEvent.click(screen.getByTestId('join-password-cancel'));
    await fixture.whenStable();

    expect(joinGame).not.toHaveBeenCalled();
    expect(nav).not.toHaveBeenCalled();
    expect(screen.queryByTestId('join-password-dialog')).toBeNull();
  });
});
