import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../core/auth.service';
import { MeResponse } from '../core/models';
import { Card } from '../features/game/game.models';
import { CardSetManifest } from './card-set.models';
import { CardSetService } from './card-set.service';

const ASSO: Card = { suit: 'Bastoni', rank: 'Asso' };

const PIACENTINE: CardSetManifest = {
  id: 'piacentine',
  name: 'Piacentine',
  license: 'Public Domain',
  preview: 'preview.png',
  fileExtension: 'svg',
  filePattern: '{suit}-{rank}.{ext}',
  back: 'back.svg',
};

interface AuthStub {
  user: ReturnType<typeof signal<MeResponse | null>>;
  service: AuthService;
}

function authStub(initial: MeResponse | null = null): AuthStub {
  const user = signal<MeResponse | null>(initial);
  const service = {
    currentUser: () => user(),
  } as unknown as AuthService;
  return { user, service };
}

function setup(opts: { auth?: AuthStub } = {}) {
  const auth = opts.auth ?? authStub();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: auth.service },
      CardSetService,
    ],
  });
  return {
    svc: TestBed.inject(CardSetService),
    ctrl: TestBed.inject(HttpTestingController),
    appRef: TestBed.inject(ApplicationRef),
    auth,
  };
}

function flush(appRef: ApplicationRef) {
  // Drive Angular's effect scheduler.
  appRef.tick();
}

function meResponse(opts: Partial<MeResponse> = {}): MeResponse {
  return {
    id: 'u1',
    username: 'alice',
    displayName: 'Alice',
    email: 'a@a',
    activeCardSetId: 'placeholder',
    ranking: { elo: 1000, wins: 0, losses: 0, draws: 0, gamesPlayed: 0, updatedAt: '' },
    ...opts,
  };
}

describe('CardSetService.activeSet', () => {
  it('defaults to placeholder and resolves lowercase suit/rank URLs', () => {
    const { svc } = setup();
    expect(svc.activeSetId()).toBe('placeholder');
    expect(svc.activeSet().resolveFront(ASSO)).toBe('/card-sets/placeholder/bastoni-asso.svg');
    expect(svc.activeSet().resolveBack()).toBe('/card-sets/placeholder/back.svg');
  });

  it('switches asset URLs when the active set changes (no auth)', async () => {
    const { svc, ctrl } = setup();
    const p = svc.loadManifests();
    ctrl.expectOne('/api/v1/card-sets').flush([PIACENTINE]);
    await p;
    await svc.setActiveSet('piacentine');
    expect(svc.activeSet().resolveFront(ASSO)).toBe('/card-sets/piacentine/bastoni-asso.svg');
  });

  it('falls back to placeholder when the active set id is unknown', async () => {
    const { svc } = setup();
    await svc.setActiveSet('does-not-exist');
    expect(svc.activeSet().id).toBe('placeholder');
  });
});

describe('CardSetService.loadManifests', () => {
  it('replaces the cached list with the server response', async () => {
    const { svc, ctrl } = setup();
    const p = svc.loadManifests();
    ctrl.expectOne('/api/v1/card-sets').flush([PIACENTINE]);
    await p;
    const ids = svc.manifests().map((m) => m.id);
    expect(ids).toContain('piacentine');
    // Placeholder is preserved even if the server omits it.
    expect(ids).toContain('placeholder');
    ctrl.verify();
  });

  it('keeps the placeholder default if the request fails', async () => {
    const { svc, ctrl } = setup();
    const p = svc.loadManifests();
    ctrl.expectOne('/api/v1/card-sets').flush(null, { status: 500, statusText: 'fail' });
    await p;
    expect(svc.manifests().map((m) => m.id)).toEqual(['placeholder']);
    ctrl.verify();
  });
});

describe('CardSetService bootstrap from authenticated user', () => {
  it('seeds the active set from currentUser.activeCardSetId and fetches manifests', async () => {
    const auth = authStub(null);
    const { svc, ctrl, appRef } = setup({ auth });

    flush(appRef);
    ctrl.expectNone('/api/v1/card-sets');
    expect(svc.activeSetId()).toBe('placeholder');

    // Login arrives.
    auth.user.set(meResponse({ activeCardSetId: 'piacentine' }));
    flush(appRef);
    expect(svc.activeSetId()).toBe('piacentine');

    const req = ctrl.expectOne('/api/v1/card-sets');
    req.flush([PIACENTINE]);
    await Promise.resolve();
    ctrl.verify();
  });

  it('does not refetch when the same user keeps the session open', async () => {
    const auth = authStub(meResponse({ activeCardSetId: 'placeholder' }));
    const { ctrl, appRef } = setup({ auth });
    flush(appRef);
    ctrl.expectOne('/api/v1/card-sets').flush([]);
    await Promise.resolve();
    // Re-publish the same user — effect should not fire a second request.
    auth.user.set(meResponse({ activeCardSetId: 'placeholder' }));
    flush(appRef);
    ctrl.expectNone('/api/v1/card-sets');
  });

  it('re-bootstraps after a logout/login cycle', async () => {
    const auth = authStub(meResponse({ activeCardSetId: 'placeholder' }));
    const { ctrl, appRef } = setup({ auth });
    flush(appRef);
    ctrl.expectOne('/api/v1/card-sets').flush([]);
    await Promise.resolve();

    auth.user.set(null); // logout
    flush(appRef);
    auth.user.set(meResponse({ activeCardSetId: 'piacentine' })); // fresh login
    flush(appRef);
    ctrl.expectOne('/api/v1/card-sets').flush([PIACENTINE]);
    ctrl.verify();
  });
});

describe('CardSetService.setActiveSet persistence', () => {
  it('PATCHes /me when the user is authenticated', async () => {
    const auth = authStub(meResponse());
    const { svc, ctrl, appRef } = setup({ auth });
    flush(appRef);
    // The bootstrap also fetches manifests; drain it.
    ctrl.expectOne('/api/v1/card-sets').flush([PIACENTINE]);
    await Promise.resolve();

    const p = svc.setActiveSet('piacentine');
    const req = ctrl.expectOne('/api/v1/me');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ activeCardSetId: 'piacentine' });
    req.flush({}, { status: 200, statusText: 'OK' });
    await p;
    expect(svc.activeSetId()).toBe('piacentine');
    ctrl.verify();
  });

  it('reverts the local change if the PATCH fails', async () => {
    const auth = authStub(meResponse());
    const { svc, ctrl, appRef } = setup({ auth });
    flush(appRef);
    ctrl.expectOne('/api/v1/card-sets').flush([PIACENTINE]);
    await Promise.resolve();

    const before = svc.activeSetId();
    const p = svc.setActiveSet('piacentine');
    ctrl.expectOne('/api/v1/me').flush({}, { status: 500, statusText: 'fail' });
    await expect(p).rejects.toMatchObject({ status: 500 });
    expect(svc.activeSetId()).toBe(before);
  });

  it('does not PATCH when no user is authenticated', async () => {
    const { svc, ctrl } = setup();
    await svc.setActiveSet('piacentine');
    ctrl.expectNone('/api/v1/me');
    expect(svc.activeSetId()).toBe('piacentine');
  });

  it('is a no-op when the requested set is already active', async () => {
    const auth = authStub(meResponse({ activeCardSetId: 'placeholder' }));
    const { svc, ctrl, appRef } = setup({ auth });
    flush(appRef);
    ctrl.expectOne('/api/v1/card-sets').flush([]);
    await Promise.resolve();

    await svc.setActiveSet('placeholder');
    ctrl.expectNone('/api/v1/me');
  });
});

describe('CardSetService fallback', () => {
  let warn: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => warn.mockRestore());

  it('warns once per (set, card) pair on front fallback', () => {
    const { svc } = setup();
    expect(svc.fallbackFrontUrl(ASSO, 'piacentine')).toBe(
      '/card-sets/placeholder/bastoni-asso.svg',
    );
    expect(svc.fallbackFrontUrl(ASSO, 'piacentine')).toBe(
      '/card-sets/placeholder/bastoni-asso.svg',
    );
    expect(warn).toHaveBeenCalledTimes(1);
  });

  it('warns once for a missing back asset', () => {
    const { svc } = setup();
    svc.fallbackBackUrl('piacentine');
    svc.fallbackBackUrl('piacentine');
    expect(warn).toHaveBeenCalledTimes(1);
  });
});
