import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { CardSetManifest } from '../../card-sets/card-set.models';
import { CardSetService } from '../../card-sets/card-set.service';
import { AuthService } from '../../core/auth.service';
import { ErrorToastService } from '../../core/error-toast.service';
import { MeResponse } from '../../core/models';
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

async function setup(
  opts: {
    manifests?: CardSetManifest[];
    active?: string;
    setActive?: (id: string) => Promise<void>;
  } = {},
) {
  const cardSets = cardSetStub(opts);
  const toastError = vi.fn();
  const toast = { error: toastError } as unknown as ErrorToastService;
  const r = await render(ProfileComponent, {
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: authStub() },
      { provide: CardSetService, useValue: cardSets.svc },
      { provide: ErrorToastService, useValue: toast },
    ],
  });
  return { ...r, ...cardSets, toastError };
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
