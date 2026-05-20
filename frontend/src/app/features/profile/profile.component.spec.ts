import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { CardSetManifest } from '../../card-sets/card-set.models';
import { CardSetService } from '../../card-sets/card-set.service';
import { AuthService } from '../../core/auth.service';
import { ErrorToastService } from '../../core/error-toast.service';
import { MatchHistoryPage, MeResponse } from '../../core/models';
import { MatchHistoryService } from './history.service';
import { ProfileComponent } from './profile.component';

const PLACEHOLDER: CardSetManifest = {
  id: 'placeholder',
  name: 'Placeholder',
  license: 'Bundled',
  preview: 'preview.svg',
  fileExtension: 'svg',
  filePattern: '{suit}-{rank}.{ext}',
  back: 'back.svg',
};

const PIACENTINE: CardSetManifest = {
  id: 'piacentine',
  name: 'Piacentine',
  license: 'Public Domain',
  preview: 'preview.png',
  fileExtension: 'svg',
  filePattern: '{suit}-{rank}.{ext}',
  back: 'back.svg',
};

function authStub(
  overrides: Partial<{
    changeEmail: (req: { currentPassword: string; newEmail: string }) => Promise<void>;
    changePassword: (req: { currentPassword: string; newPassword: string }) => Promise<void>;
    logout: () => Promise<void>;
  }> = {},
): AuthService {
  const me: MeResponse = {
    id: 'u1',
    username: 'alice',
    displayName: 'Alice',
    email: 'a@a',
    activeCardSetId: 'placeholder',
    ranking: { elo: 1000, wins: 0, losses: 0, draws: 0, gamesPlayed: 0, updatedAt: '' },
  };
  return {
    currentUser: () => me,
    // ProfileComponent.ngOnInit refreshes /me on mount so the ranking
    // widget doesn't lag behind. Stub returns the cached value.
    refreshMe: () => Promise.resolve(me),
    changeEmail: overrides.changeEmail ?? vi.fn(() => Promise.resolve()),
    changePassword: overrides.changePassword ?? vi.fn(() => Promise.resolve()),
    logout: overrides.logout ?? vi.fn(() => Promise.resolve()),
  } as unknown as AuthService;
}

function cardSetStub(opts: {
  manifests?: CardSetManifest[];
  active?: string;
  setActive?: (id: string) => Promise<void>;
}) {
  const manifestsSig = signal<readonly CardSetManifest[]>(
    opts.manifests ?? [PLACEHOLDER, PIACENTINE],
  );
  const activeSig = signal<string>(opts.active ?? 'placeholder');
  const setActiveSet = vi.fn(
    opts.setActive ??
      ((id: string) => {
        activeSig.set(id);
        return Promise.resolve();
      }),
  );
  const svc = {
    manifests: () => manifestsSig(),
    activeSetId: () => activeSig(),
    setActiveSet,
  } as unknown as CardSetService;
  return { svc, manifestsSig, activeSig, setActiveSet };
}

function historyStub(opts: { pages?: MatchHistoryPage[]; rejectOnLoad?: boolean }): {
  svc: MatchHistoryService;
  loadPage: ReturnType<typeof vi.fn>;
} {
  const pages = opts.pages ?? [emptyHistoryPage()];
  let calls = 0;
  const loadPage = vi.fn(async (page: number) => {
    if (opts.rejectOnLoad) {
      throw new Error('boom');
    }
    const idx = Math.min(calls, pages.length - 1);
    calls++;
    return { ...pages[idx], page } as MatchHistoryPage;
  });
  const svc = { loadPage } as unknown as MatchHistoryService;
  return { svc, loadPage };
}

function emptyHistoryPage(): MatchHistoryPage {
  return { items: [], page: 1, pageSize: 10, totalCount: 0 };
}

async function setup(
  opts: {
    manifests?: CardSetManifest[];
    active?: string;
    setActive?: (id: string) => Promise<void>;
    historyPages?: MatchHistoryPage[];
    historyRejects?: boolean;
    auth?: AuthService;
  } = {},
) {
  const cardSets = cardSetStub(opts);
  const history = historyStub({ pages: opts.historyPages, rejectOnLoad: opts.historyRejects });
  const toastError = vi.fn();
  const toastSuccess = vi.fn();
  const toast = {
    error: toastError,
    success: toastSuccess,
  } as unknown as ErrorToastService;
  const auth = opts.auth ?? authStub();
  const r = await render(ProfileComponent, {
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: auth },
      { provide: CardSetService, useValue: cardSets.svc },
      { provide: MatchHistoryService, useValue: history.svc },
      { provide: ErrorToastService, useValue: toast },
    ],
  });
  return { ...r, ...cardSets, ...history, toastError, toastSuccess, auth };
}

describe('ProfileComponent', () => {
  it('renders one tile per registered card set', async () => {
    await setup();
    const tiles = screen.getAllByTestId('card-set-tile');
    expect(tiles).toHaveLength(2);
    expect(tiles[0]?.getAttribute('data-set-id')).toBe('placeholder');
    expect(tiles[1]?.getAttribute('data-set-id')).toBe('piacentine');
  });

  it('marks the active set with the Active badge and the .active class', async () => {
    await setup({ active: 'piacentine' });
    const tiles = screen.getAllByTestId('card-set-tile');
    expect(tiles[0]?.classList.contains('active')).toBe(false);
    expect(tiles[1]?.classList.contains('active')).toBe(true);
    expect(screen.getByTestId('active-badge')).toBeInTheDocument();
  });

  it('renders the preview from /card-sets/<id>/<preview>', async () => {
    await setup();
    const imgs = screen.getAllByTestId('card-set-tile').map((t) => t.querySelector('img.preview'));
    expect(imgs[0]?.getAttribute('src')).toBe('/card-sets/placeholder/preview.svg');
    expect(imgs[1]?.getAttribute('src')).toBe('/card-sets/piacentine/preview.png');
  });

  it('calls setActiveSet on click for a non-active tile', async () => {
    const { setActiveSet, fixture } = await setup({ active: 'placeholder' });
    const piacentineTile = screen.getAllByTestId('card-set-tile')[1];
    if (!piacentineTile) throw new Error('test setup');
    fireEvent.click(piacentineTile);
    await fixture.whenStable();
    expect(setActiveSet).toHaveBeenCalledWith('piacentine');
  });

  it('does not call setActiveSet when clicking the already-active tile', async () => {
    const { setActiveSet } = await setup({ active: 'placeholder' });
    const tile = screen.getAllByTestId('card-set-tile')[0];
    if (!tile) throw new Error('test setup');
    fireEvent.click(tile);
    expect(setActiveSet).not.toHaveBeenCalled();
  });

  it('falls back to the placeholder preview when a tile image 404s', async () => {
    await setup();
    const piacentineImg = screen
      .getAllByTestId('card-set-tile')[1]
      ?.querySelector('img.preview') as HTMLImageElement | null;
    if (!piacentineImg) throw new Error('test setup');
    expect(piacentineImg.getAttribute('src')).toBe('/card-sets/piacentine/preview.png');
    piacentineImg.dispatchEvent(new Event('error'));
    expect(piacentineImg.getAttribute('src')).toBe('/card-sets/placeholder/preview.svg');

    // Bouncing the same error again must not loop.
    const afterFallback = piacentineImg.getAttribute('src');
    piacentineImg.dispatchEvent(new Event('error'));
    expect(piacentineImg.getAttribute('src')).toBe(afterFallback);
  });

  it('shows a toast when setActiveSet rejects', async () => {
    const { toastError, fixture } = await setup({
      active: 'placeholder',
      setActive: () => Promise.reject(new Error('boom')),
    });
    const piacentineTile = screen.getAllByTestId('card-set-tile')[1];
    if (!piacentineTile) throw new Error('test setup');
    fireEvent.click(piacentineTile);
    await fixture.whenStable();
    expect(toastError).toHaveBeenCalled();
  });
});

describe('ProfileComponent ranking section', () => {
  it('renders Elo and W/L/D counters from the current user', async () => {
    await setup();
    const grid = screen.getByTestId('ranking-grid');
    expect(grid).toHaveTextContent(/elo/i);
    expect(grid).toHaveTextContent('1000');
    expect(grid).toHaveTextContent(/wins?/i);
    expect(grid).toHaveTextContent(/draws?/i);
  });
});

describe('ProfileComponent match history', () => {
  it('shows the empty state when the user has no Finished games', async () => {
    const { fixture } = await setup();
    await fixture.whenStable();
    expect(screen.getByTestId('history-empty')).toBeInTheDocument();
  });

  it('renders one row per history entry with the player-relative result chip', async () => {
    const page: MatchHistoryPage = {
      page: 1,
      pageSize: 10,
      totalCount: 2,
      items: [
        {
          gameId: 'g1',
          mode: 'TwoPlayer',
          name: 'win-game',
          startedAt: '2026-05-17T10:00:00Z',
          endedAt: '2026-05-17T10:30:00Z',
          mySeatIndex: 0,
          seatUserIds: ['u1', 'u2'],
          outcomeKind: 'Win',
          winnerKey: 0,
          seatScores: [80, 40],
          teamScores: null,
          reason: 'Normal',
        },
        {
          gameId: 'g2',
          mode: 'TwoPlayer',
          name: 'loss-game',
          startedAt: '2026-05-17T09:00:00Z',
          endedAt: '2026-05-17T09:30:00Z',
          mySeatIndex: 0,
          seatUserIds: ['u1', 'u2'],
          outcomeKind: 'Win',
          winnerKey: 1,
          seatScores: [50, 70],
          teamScores: null,
          reason: 'Normal',
        },
      ],
    };
    const { fixture } = await setup({ historyPages: [page] });
    await fixture.whenStable();

    const rows = screen.getAllByTestId('history-row');
    expect(rows).toHaveLength(2);
    expect(rows[0]?.getAttribute('data-result')).toBe('win');
    expect(rows[1]?.getAttribute('data-result')).toBe('loss');
    const scores = screen.getAllByTestId('history-score').map((el) => el.textContent?.trim());
    expect(scores[0]).toContain('80');
    expect(scores[0]).toContain('40');
  });

  it('uses team math for 4-player games', async () => {
    const page: MatchHistoryPage = {
      page: 1,
      pageSize: 10,
      totalCount: 1,
      items: [
        {
          gameId: 'g',
          mode: 'FourPlayerTeams',
          name: '4p',
          startedAt: null,
          endedAt: '2026-05-17T10:00:00Z',
          mySeatIndex: 2,
          seatUserIds: ['u1', 'u2', 'u3', 'u4'],
          outcomeKind: 'Win',
          winnerKey: 0,
          seatScores: [30, 20, 40, 30],
          teamScores: [70, 50],
          reason: 'Normal',
        },
      ],
    };
    const { fixture } = await setup({ historyPages: [page] });
    await fixture.whenStable();
    const row = screen.getByTestId('history-row');
    // seat 2 is on team 0 (winnerKey 0) → win
    expect(row.getAttribute('data-result')).toBe('win');
    expect(screen.getByTestId('history-score').textContent).toContain('70');
    expect(screen.getByTestId('history-score').textContent).toContain('50');
  });

  it('renders pager and loads the next page on click', async () => {
    const pages: MatchHistoryPage[] = [
      {
        page: 1,
        pageSize: 10,
        totalCount: 15,
        items: [historyEntry('a')],
      },
      {
        page: 2,
        pageSize: 10,
        totalCount: 15,
        items: [historyEntry('b')],
      },
    ];
    const { fixture, loadPage } = await setup({ historyPages: pages });
    await fixture.whenStable();
    expect(screen.getByTestId('history-page-label')).toHaveTextContent(/1.*2/);

    fireEvent.click(screen.getByTestId('history-next'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(loadPage).toHaveBeenLastCalledWith(2, 10);
  });

  it('shows a toast when the history request fails', async () => {
    const { toastError, fixture } = await setup({ historyRejects: true });
    await fixture.whenStable();
    expect(toastError).toHaveBeenCalled();
  });

  it('shows the error panel + retry button when the load fails', async () => {
    const { fixture } = await setup({ historyRejects: true });
    await fixture.whenStable();
    expect(screen.getByTestId('history-error')).toBeInTheDocument();
    expect(screen.getByTestId('history-retry')).toBeInTheDocument();
    // Empty/list views must not render while the section is in the error state.
    expect(screen.queryByTestId('history-empty')).toBeNull();
    expect(screen.queryByTestId('history-list')).toBeNull();
  });
});

describe('ProfileComponent security section', () => {
  it('renders the change-email + change-password forms', async () => {
    await setup();
    expect(screen.getByTestId('change-email-form')).toBeInTheDocument();
    expect(screen.getByTestId('change-password-form')).toBeInTheDocument();
  });

  it('disables the change-email submit until the form is valid', async () => {
    await setup();
    const btn = screen.getByTestId('change-email-submit') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    const newEmail = screen.getByTestId('change-email-new') as HTMLInputElement;
    const pwd = screen.getByTestId('change-email-current-password') as HTMLInputElement;
    const { fireEvent } = await import('@testing-library/angular');
    fireEvent.input(newEmail, { target: { value: 'new@example.com' } });
    fireEvent.input(pwd, { target: { value: 'Strong-Pass-123' } });
    expect(btn.disabled).toBe(false);
  });

  it('calls AuthService.changeEmail with the entered values + logs out on success', async () => {
    const changeEmail = vi.fn(() => Promise.resolve());
    const logout = vi.fn(() => Promise.resolve());
    const auth = authStub({ changeEmail, logout });
    const { fixture, toastSuccess } = await setup({ auth });

    const { fireEvent } = await import('@testing-library/angular');
    fireEvent.input(screen.getByTestId('change-email-new'), {
      target: { value: 'new@example.com' },
    });
    fireEvent.input(screen.getByTestId('change-email-current-password'), {
      target: { value: 'Strong-Pass-123' },
    });
    fireEvent.click(screen.getByTestId('change-email-submit'));
    await fixture.whenStable();

    expect(changeEmail).toHaveBeenCalledWith({
      currentPassword: 'Strong-Pass-123',
      newEmail: 'new@example.com',
    });
    expect(logout).toHaveBeenCalled();
    expect(toastSuccess).toHaveBeenCalled();
  });

  it('maps an InvalidCurrentPassword error to the i18n key in a toast', async () => {
    const changeEmail = vi.fn(() =>
      Promise.reject({ status: 400, error: { code: 'InvalidCurrentPassword' } }),
    );
    const logout = vi.fn(() => Promise.resolve());
    const auth = authStub({ changeEmail, logout });
    const { fixture, toastError } = await setup({ auth });

    const { fireEvent } = await import('@testing-library/angular');
    fireEvent.input(screen.getByTestId('change-email-new'), {
      target: { value: 'new@example.com' },
    });
    fireEvent.input(screen.getByTestId('change-email-current-password'), {
      target: { value: 'wrong' },
    });
    fireEvent.click(screen.getByTestId('change-email-submit'));
    await fixture.whenStable();

    expect(toastError).toHaveBeenCalled();
    // Logout should NOT happen on failure.
    expect(logout).not.toHaveBeenCalled();
  });

  it('requires confirm-password to match new-password before enabling submit', async () => {
    await setup();
    const btn = screen.getByTestId('change-password-submit') as HTMLButtonElement;
    const { fireEvent } = await import('@testing-library/angular');
    fireEvent.input(screen.getByTestId('change-password-current'), {
      target: { value: 'Strong-Pass-123' },
    });
    fireEvent.input(screen.getByTestId('change-password-new'), {
      target: { value: 'Different-Pass-456' },
    });
    fireEvent.input(screen.getByTestId('change-password-confirm'), {
      target: { value: 'Mismatched-Pass' },
    });
    expect(btn.disabled).toBe(true);
    fireEvent.input(screen.getByTestId('change-password-confirm'), {
      target: { value: 'Different-Pass-456' },
    });
    expect(btn.disabled).toBe(false);
  });

  it('calls AuthService.changePassword + logs out on success', async () => {
    const changePassword = vi.fn(() => Promise.resolve());
    const logout = vi.fn(() => Promise.resolve());
    const auth = authStub({ changePassword, logout });
    const { fixture, toastSuccess } = await setup({ auth });

    const { fireEvent } = await import('@testing-library/angular');
    fireEvent.input(screen.getByTestId('change-password-current'), {
      target: { value: 'Strong-Pass-123' },
    });
    fireEvent.input(screen.getByTestId('change-password-new'), {
      target: { value: 'Different-Pass-456' },
    });
    fireEvent.input(screen.getByTestId('change-password-confirm'), {
      target: { value: 'Different-Pass-456' },
    });
    fireEvent.click(screen.getByTestId('change-password-submit'));
    await fixture.whenStable();

    expect(changePassword).toHaveBeenCalledWith({
      currentPassword: 'Strong-Pass-123',
      newPassword: 'Different-Pass-456',
    });
    expect(logout).toHaveBeenCalled();
    expect(toastSuccess).toHaveBeenCalled();
  });
});

function historyEntry(suffix: string): MatchHistoryPage['items'][number] {
  return {
    gameId: `g-${suffix}`,
    mode: 'TwoPlayer',
    name: `game-${suffix}`,
    startedAt: null,
    endedAt: '2026-05-17T10:00:00Z',
    mySeatIndex: 0,
    seatUserIds: ['u1', 'u2'],
    outcomeKind: 'Win',
    winnerKey: 0,
    seatScores: [60, 60],
    teamScores: null,
    reason: 'Normal',
  };
}
