import { render, screen } from '@testing-library/angular';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReconnectBannerComponent } from './reconnect-banner.component';

const NOW = new Date('2026-05-17T12:00:00Z');

async function setup(opts: { deadline: Date | null; seatIndex?: number | null }) {
  return render(ReconnectBannerComponent, {
    inputs: { deadline: opts.deadline, seatIndex: opts.seatIndex ?? null },
  });
}

describe('ReconnectBannerComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('is hidden when no deadline is set', async () => {
    await setup({ deadline: null });
    expect(screen.queryByTestId('reconnect-banner')).toBeNull();
  });

  it('shows the initial seconds remaining for an active deadline', async () => {
    await setup({
      deadline: new Date(NOW.getTime() + 10_000),
      seatIndex: 1,
    });
    expect(screen.getByTestId('reconnect-banner')).toBeInTheDocument();
    expect(screen.getByTestId('reconnect-countdown')).toHaveTextContent(/10s/);
  });

  it('counts down as time advances', async () => {
    const r = await setup({
      deadline: new Date(NOW.getTime() + 10_000),
      seatIndex: 0,
    });
    vi.advanceTimersByTime(3_000);
    r.fixture.detectChanges();
    expect(screen.getByTestId('reconnect-countdown')).toHaveTextContent(/7s/);
    vi.advanceTimersByTime(5_000);
    r.fixture.detectChanges();
    expect(screen.getByTestId('reconnect-countdown')).toHaveTextContent(/2s/);
  });

  it('hides the banner once the deadline has passed', async () => {
    const r = await setup({
      deadline: new Date(NOW.getTime() + 2_000),
      seatIndex: 0,
    });
    vi.advanceTimersByTime(3_000);
    r.fixture.detectChanges();
    expect(screen.queryByTestId('reconnect-banner')).toBeNull();
  });
});
