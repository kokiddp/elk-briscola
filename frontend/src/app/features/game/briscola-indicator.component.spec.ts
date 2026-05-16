import { render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { BriscolaIndicatorComponent } from './briscola-indicator.component';
import { Card } from './game.models';

const RE_DENARI: Card = { suit: 'Denari', rank: 'Re' };

function cardSetsStub(): CardSetService {
  return {
    activeSet: () => ({
      id: 'p',
      name: 'p',
      resolveFront: (c: Card) => `/p/${c.suit}-${c.rank}.svg`,
      resolveBack: () => '/p/back.svg',
    }),
    activeSetId: () => 'p',
    fallbackFrontUrl: (c: Card) => `/p/${c.suit}-${c.rank}.svg`,
    fallbackBackUrl: () => '/p/back.svg',
  } as unknown as CardSetService;
}

async function setup(opts: { stockCount: number; card?: Card }) {
  return render(BriscolaIndicatorComponent, {
    providers: [{ provide: CardSetService, useValue: cardSetsStub() }],
    inputs: { briscolaCard: opts.card ?? RE_DENARI, stockCount: opts.stockCount },
  });
}

describe('BriscolaIndicatorComponent', () => {
  it('renders visible when stock is non-empty', async () => {
    await setup({ stockCount: 30 });
    const el = screen.getByTestId('briscola-indicator');
    expect(el.classList.contains('faded')).toBe(false);
  });

  it('fades out when the stock empties', async () => {
    await setup({ stockCount: 0 });
    expect(screen.getByTestId('briscola-indicator').classList.contains('faded')).toBe(true);
  });

  it('renders the briscola card', async () => {
    await setup({ stockCount: 30 });
    const img = screen.getByTestId('card') as HTMLImageElement;
    expect(img.getAttribute('src')).toBe('/p/Denari-Re.svg');
  });
});
