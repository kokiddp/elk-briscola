import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { Card, PlayedCard } from './game.models';
import { TrickAreaComponent } from './trick-area.component';

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

async function setup(plays: PlayedCard[]) {
  return render(TrickAreaComponent, {
    providers: [provideNoopAnimations(), { provide: CardSetService, useValue: cardSetsStub() }],
    inputs: { plays },
  });
}

describe('TrickAreaComponent', () => {
  it('renders one played-card per play, preserving order', async () => {
    await setup([
      { seatIndex: 0, card: { suit: 'Bastoni', rank: 'Asso' } },
      { seatIndex: 1, card: { suit: 'Coppe', rank: 'Tre' } },
    ]);
    const cards = screen.getAllByTestId('played-card');
    expect(cards).toHaveLength(2);
    expect(cards[0]?.getAttribute('data-seat')).toBe('0');
    expect(cards[1]?.getAttribute('data-seat')).toBe('1');
  });

  it('renders an empty trick when no plays', async () => {
    await setup([]);
    expect(screen.queryAllByTestId('played-card')).toHaveLength(0);
    expect(screen.getByTestId('trick-area')).toBeInTheDocument();
  });
});
