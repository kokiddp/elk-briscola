import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Card } from '../features/game/game.models';
import { CardSetManifest } from './card-set.models';
import { CardSetService } from './card-set.service';

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), CardSetService],
  });
  return {
    svc: TestBed.inject(CardSetService),
    ctrl: TestBed.inject(HttpTestingController),
  };
}

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

describe('CardSetService.activeSet', () => {
  it('defaults to placeholder and resolves lowercase suit/rank URLs', () => {
    const { svc } = setup();
    expect(svc.activeSetId()).toBe('placeholder');
    expect(svc.activeSet().resolveFront(ASSO)).toBe('/card-sets/placeholder/bastoni-asso.svg');
    expect(svc.activeSet().resolveBack()).toBe('/card-sets/placeholder/back.svg');
  });

  it('switches asset URLs when the active set changes', async () => {
    const { svc, ctrl } = setup();
    const p = svc.loadManifests();
    ctrl.expectOne('/api/v1/card-sets').flush([PIACENTINE]);
    await p;
    svc.setActiveSet('piacentine');
    expect(svc.activeSet().resolveFront(ASSO)).toBe('/card-sets/piacentine/bastoni-asso.svg');
  });

  it('falls back to placeholder when the active set id is unknown', () => {
    const { svc } = setup();
    svc.setActiveSet('does-not-exist');
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
