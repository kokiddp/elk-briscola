import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { EndGameDialogComponent } from './end-game-dialog.component';
import { GameFinishedEvent, GameMode } from './game.models';

function makeResult(opts: Partial<GameFinishedEvent> = {}): GameFinishedEvent {
  return {
    outcome: { kind: 'Winner', winnerKey: 0 },
    seatScores: [70, 50],
    reason: 'Normal',
    ...opts,
  };
}

async function setup(opts: {
  result?: GameFinishedEvent;
  mode?: GameMode;
  mySeat?: number | null;
  isSpectator?: boolean;
}) {
  const onBack = vi.fn<() => void>();
  const r = await render(EndGameDialogComponent, {
    inputs: {
      result: opts.result ?? makeResult(),
      mode: opts.mode ?? 'TwoPlayer',
      mySeat: opts.mySeat ?? null,
      isSpectator: opts.isSpectator ?? false,
    },
    on: { backToLobby: () => onBack() },
  });
  return { ...r, onBack };
}

describe('EndGameDialogComponent', () => {
  it('shows the "You won" banner when my seat matches the winner', async () => {
    await setup({ mySeat: 0 });
    expect(screen.getByTestId('end-banner')).toHaveTextContent(/won/i);
  });

  it('shows the "You lost" banner when my seat differs from the winner', async () => {
    await setup({ mySeat: 1 });
    expect(screen.getByTestId('end-banner')).toHaveTextContent(/lost/i);
  });

  it('shows the "Draw" banner when the outcome is a draw', async () => {
    await setup({
      result: makeResult({ outcome: { kind: 'Draw', winnerKey: null } }),
      mySeat: 0,
    });
    expect(screen.getByTestId('end-banner')).toHaveTextContent(/draw/i);
  });

  it('renders 2p per-seat scores', async () => {
    await setup({ mySeat: 0 });
    const rows = screen.getByTestId('end-scores').querySelectorAll('tr');
    expect(rows.length).toBe(2);
  });

  it('renders 4p team-summed scores', async () => {
    await setup({
      mode: 'FourPlayerTeams',
      mySeat: 2,
      result: makeResult({
        outcome: { kind: 'Winner', winnerKey: 0 },
        seatScores: [30, 10, 25, 15],
      }),
    });
    const cells = screen.getByTestId('end-scores').querySelectorAll('td');
    expect(cells[0]?.textContent?.trim()).toBe('55');
    expect(cells[1]?.textContent?.trim()).toBe('25');
    // Seat 2 is on Team A (winnerKey 0) — should read as a win.
    expect(screen.getByTestId('end-banner')).toHaveTextContent(/won/i);
  });

  it('emits backToLobby when the back button is clicked', async () => {
    const { onBack } = await setup({ mySeat: 0 });
    fireEvent.click(screen.getByTestId('end-back-to-lobby'));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('shows the neutral "Game ended" banner for spectators', async () => {
    // Even when the spectator would otherwise be "seated at 0" (i.e.
    // the winning seat), the spectator banner must never say "You won".
    await setup({ mySeat: 0, isSpectator: true });
    expect(screen.getByTestId('end-banner')).toHaveTextContent(/ended/i);
    expect(screen.getByTestId('end-banner')).not.toHaveTextContent(/won|lost/i);
  });

  it('shows the forfeit-disconnect reason label', async () => {
    await setup({
      result: makeResult({ reason: 'ForfeitDisconnect' }),
      mySeat: 0,
    });
    expect(screen.getByTestId('end-reason')).toHaveTextContent(/disconnected/i);
  });
});
