import { render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { Card } from './game.models';
import { StockComponent } from './stock.component';

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

async function setup(count: number) {
  return render(StockComponent, {
    providers: [{ provide: CardSetService, useValue: cardSetsStub() }],
    inputs: { count },
  });
}

describe('StockComponent', () => {
  it('shows the exact count in the badge', async () => {
    await setup(7);
    expect(screen.getByTestId('stock-count')).toHaveTextContent('7');
  });

  it('caps the rendered stack at 5 backs', async () => {
    await setup(40);
    expect(screen.getByTestId('stock-stack').querySelectorAll('bri-card').length).toBe(5);
  });

  it('renders no backs when count is zero', async () => {
    await setup(0);
    expect(screen.getByTestId('stock-stack').querySelectorAll('bri-card').length).toBe(0);
    expect(screen.getByTestId('stock').classList.contains('empty')).toBe(true);
  });
});
