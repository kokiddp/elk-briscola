import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { AuthService } from '../../core/auth.service';
import { LobbyComponent } from './lobby.component';
import { GameSummary } from './lobby.models';
import { LobbyService } from './lobby.service';

function makeAuth(): AuthService {
  return {
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

function makeLobby(opts: { open?: GameSummary[]; running?: GameSummary[] }): LobbyService {
  const openSig = signal<readonly GameSummary[]>(opts.open ?? []);
  const runningSig = signal<readonly GameSummary[]>(opts.running ?? []);
  return {
    openGames: () => openSig(),
    runningGames: () => runningSig(),
    connect: () => Promise.resolve(),
    disconnect: () => Promise.resolve(),
    createGame: () => Promise.resolve({} as never),
    joinGame: () => Promise.resolve({} as never),
    leaveGame: () => Promise.resolve(),
  } as unknown as LobbyService;
}

async function setup(opts: { open?: GameSummary[]; running?: GameSummary[] }) {
  const lobby = makeLobby(opts);
  const r = await render(LobbyComponent, {
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: '**', component: LobbyComponent }]),
      { provide: AuthService, useValue: makeAuth() },
      { provide: LobbyService, useValue: lobby },
    ],
  });
  return { ...r, lobby };
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

describe('LobbyComponent', () => {
  it('renders the title and create button', async () => {
    await setup({});
    expect(screen.getByRole('heading', { name: /lobby/i, level: 1 })).toBeInTheDocument();
    expect(screen.getByTestId('create-game')).toBeInTheDocument();
  });

  it('shows the empty state when there are no open games', async () => {
    await setup({});
    expect(screen.getByText(/no open games yet/i)).toBeInTheDocument();
  });

  it('lists open games with mode chip and seat counter', async () => {
    await setup({ open: [OPEN_2P] });
    expect(screen.getByTestId('open-list')).toBeInTheDocument();
    expect(screen.getByTestId('game-name')).toHaveTextContent('Casual match');
    expect(screen.getByTestId('mode-chip')).toHaveTextContent(/2 players/i);
    expect(screen.getByTestId('seats')).toHaveTextContent('1 / 2');
    expect(screen.getByTestId('join-button')).toBeEnabled();
  });
});
