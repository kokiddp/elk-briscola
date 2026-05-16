import { fireEvent, render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { Card, cardKey } from './game.models';
import { MyHandComponent } from './my-hand.component';

const HAND: Card[] = [
  { suit: 'Bastoni', rank: 'Asso' },
  { suit: 'Coppe', rank: 'Tre' },
  { suit: 'Denari', rank: 'Re' },
];

function cardSetsStub(): CardSetService {
  return {
    activeSet: () => ({
      id: 'placeholder',
      name: 'p',
      resolveFront: (c: Card) => `/p/${c.suit}-${c.rank}.svg`,
      resolveBack: () => '/p/back.svg',
    }),
    activeSetId: () => 'placeholder',
    fallbackFrontUrl: (c: Card) => `/p/${c.suit}-${c.rank}.svg`,
    fallbackBackUrl: () => '/p/back.svg',
  } as unknown as CardSetService;
}

async function setup(
  opts: {
    cards?: Card[];
    legal?: Set<string>;
    myTurn?: boolean;
  } = {},
) {
  const onPlay = vi.fn<(c: Card) => void>();
  const r = await render(MyHandComponent, {
    providers: [{ provide: CardSetService, useValue: cardSetsStub() }],
    inputs: {
      cards: opts.cards ?? HAND,
      legalMoves: opts.legal ?? new Set(HAND.map(cardKey)),
      myTurn: opts.myTurn ?? true,
    },
    on: { cardPlayed: (c: Card) => onPlay(c) },
  });
  return { ...r, onPlay };
}

describe('MyHandComponent', () => {
  it('renders one slot per hand card', async () => {
    await setup();
    expect(screen.getAllByTestId('hand-card')).toHaveLength(3);
  });

  it('disables every slot when myTurn is false', async () => {
    await setup({ myTurn: false });
    for (const slot of screen.getAllByTestId('hand-card') as HTMLButtonElement[]) {
      expect(slot.disabled).toBe(true);
    }
  });

  it('disables individual slots whose card is not in legalMoves', async () => {
    const onlyAsso = HAND[0];
    if (!onlyAsso) throw new Error('test setup');
    await setup({ legal: new Set([cardKey(onlyAsso)]) });
    const slots = screen.getAllByTestId('hand-card') as HTMLButtonElement[];
    expect(slots[0]?.disabled).toBe(false);
    expect(slots[1]?.disabled).toBe(true);
    expect(slots[2]?.disabled).toBe(true);
  });

  it('emits cardPlayed when a playable slot is clicked', async () => {
    const { onPlay } = await setup();
    const first = screen.getAllByTestId('hand-card')[0];
    if (!first) throw new Error('test setup');
    fireEvent.click(first);
    expect(onPlay).toHaveBeenCalledWith(HAND[0]);
  });

  it('does not emit cardPlayed for a non-playable slot', async () => {
    const { onPlay } = await setup({ myTurn: false });
    const first = screen.getAllByTestId('hand-card')[0];
    if (!first) throw new Error('test setup');
    fireEvent.click(first);
    expect(onPlay).not.toHaveBeenCalled();
  });
});
