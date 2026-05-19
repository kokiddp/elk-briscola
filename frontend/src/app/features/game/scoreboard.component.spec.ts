import { render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { ScoreboardComponent } from './scoreboard.component';

async function setup(opts: {
  mode: 'TwoPlayer' | 'FourPlayerTeams';
  seatScores: number[];
  nextToPlaySeat?: number | null;
  seatNames?: string[];
}) {
  return render(ScoreboardComponent, {
    inputs: {
      mode: opts.mode,
      seatScores: opts.seatScores,
      nextToPlaySeat: opts.nextToPlaySeat ?? null,
      seatNames: opts.seatNames ?? [],
    },
  });
}

describe('ScoreboardComponent — 2p', () => {
  it('renders one row per seat with the seat score', async () => {
    await setup({ mode: 'TwoPlayer', seatScores: [42, 37] });
    const rows = screen.getAllByTestId('score-row');
    expect(rows).toHaveLength(2);
    const values = screen.getAllByTestId('score-value').map((el) => el.textContent?.trim());
    expect(values).toEqual(['42', '37']);
  });

  it('highlights the active seat', async () => {
    await setup({ mode: 'TwoPlayer', seatScores: [10, 20], nextToPlaySeat: 1 });
    const rows = screen.getAllByTestId('score-row');
    expect(rows[0]?.classList.contains('active')).toBe(false);
    expect(rows[1]?.classList.contains('active')).toBe(true);
  });

  it('uses the supplied seat names when present', async () => {
    await setup({ mode: 'TwoPlayer', seatScores: [0, 0], seatNames: ['Alice', 'Bob'] });
    const labels = screen.getAllByTestId('score-label').map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Alice', 'Bob']);
  });
});

describe('ScoreboardComponent — 4p team math', () => {
  it('sums seats 0+2 for Team A and 1+3 for Team B', async () => {
    await setup({ mode: 'FourPlayerTeams', seatScores: [30, 10, 25, 15] });
    const labels = screen.getAllByTestId('score-label').map((el) => el.textContent?.trim());
    const values = screen.getAllByTestId('score-value').map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Team A', 'Team B']);
    expect(values).toEqual(['55', '25']);
  });

  it('labels each team with both members when seat names are available', async () => {
    await setup({
      mode: 'FourPlayerTeams',
      seatScores: [0, 0, 0, 0],
      seatNames: ['Alice', 'Bob', 'Carol', 'Dan'],
    });
    const labels = screen.getAllByTestId('score-label').map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Alice + Carol', 'Bob + Dan']);
  });

  it('highlights Team A when seat 0 or 2 is active', async () => {
    await setup({ mode: 'FourPlayerTeams', seatScores: [0, 0, 0, 0], nextToPlaySeat: 2 });
    const rows = screen.getAllByTestId('score-row');
    expect(rows[0]?.classList.contains('active')).toBe(true);
    expect(rows[1]?.classList.contains('active')).toBe(false);
  });

  it('highlights Team B when seat 1 or 3 is active', async () => {
    await setup({ mode: 'FourPlayerTeams', seatScores: [0, 0, 0, 0], nextToPlaySeat: 3 });
    const rows = screen.getAllByTestId('score-row');
    expect(rows[0]?.classList.contains('active')).toBe(false);
    expect(rows[1]?.classList.contains('active')).toBe(true);
  });

  it('handles short seatScores defensively', async () => {
    await setup({ mode: 'FourPlayerTeams', seatScores: [11, 22] });
    const values = screen.getAllByTestId('score-value').map((el) => el.textContent?.trim());
    expect(values).toEqual(['11', '22']);
  });
});
