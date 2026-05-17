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

function authStub(): AuthService {
  return {
    currentUser: () =>
      ({
        id: 'u1',
        username: 'alice',
        displayName: 'Alice',
        email: 'a@a',
        activeCardSetId: 'placeholder',
        ranking: { elo: 1000, wins: 0, losses: 0, draws: 0, gamesPlayed: 0, updatedAt: '' },
      }) as MeResponse,
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
  } = {},
) {
  const cardSets = cardSetStub(opts);
  const history = historyStub({ pages: opts.historyPages, rejectOnLoad: opts.historyRejects });
  const toastError = vi.fn();
  const toast = { error: toastError } as unknown as ErrorToastService;
  const r = await render(ProfileComponent, {
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: authStub() },
      { provide: CardSetService, useValue: cardSets.svc },
      { provide: MatchHistoryService, useValue: history.svc },
      { provide: ErrorToastService, useValue: toast },
    ],
  });
  return { ...r, ...cardSets, ...history, toastError };
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
