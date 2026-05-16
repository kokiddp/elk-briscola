import { render, screen } from '@testing-library/angular';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { CardComponent } from './card.component';
import { Card } from './game.models';

const ASSO: Card = { suit: 'Bastoni', rank: 'Asso' };

function cardSetsStub(): CardSetService {
  return {
    activeSet: () => ({
      id: 'piacentine',
      name: 'Piacentine',
      resolveFront: (c: Card) => `/card-sets/piacentine/${c.suit}-${c.rank}.svg`,
      resolveBack: () => '/card-sets/piacentine/back.svg',
    }),
    activeSetId: () => 'piacentine',
    fallbackFrontUrl: (c: Card) => `/card-sets/placeholder/${c.suit}-${c.rank}.svg`,
    fallbackBackUrl: () => '/card-sets/placeholder/back.svg',
  } as unknown as CardSetService;
}

async function renderCard(opts: { card?: Card | null; highlighted?: boolean } = {}) {
  return render(CardComponent, {
    providers: [{ provide: CardSetService, useValue: cardSetsStub() }],
    inputs: { card: opts.card ?? null, highlighted: opts.highlighted ?? false },
  });
}

describe('CardComponent', () => {
  let warn: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => warn.mockRestore());

  it('renders the front image when a card is supplied', async () => {
    await renderCard({ card: ASSO });
    const img = screen.getByTestId('card') as HTMLImageElement;
    expect(img.getAttribute('src')).toBe('/card-sets/piacentine/Bastoni-Asso.svg');
    expect(img.getAttribute('loading')).toBe('eager');
    expect(img.getAttribute('decoding')).toBe('sync');
  });

  it('renders the back image when card is null', async () => {
    await renderCard({ card: null });
    const img = screen.getByTestId('card') as HTMLImageElement;
    expect(img.getAttribute('src')).toBe('/card-sets/piacentine/back.svg');
  });

  it('swaps to the placeholder URL on image error (front)', async () => {
    await renderCard({ card: ASSO });
    const img = screen.getByTestId('card') as HTMLImageElement;
    img.dispatchEvent(new Event('error'));
    expect(img.getAttribute('src')).toBe('/card-sets/placeholder/Bastoni-Asso.svg');
  });

  it('does not loop when the fallback URL also errors', async () => {
    await renderCard({ card: ASSO });
    const img = screen.getByTestId('card') as HTMLImageElement;
    img.dispatchEvent(new Event('error'));
    const after = img.getAttribute('src');
    img.dispatchEvent(new Event('error'));
    expect(img.getAttribute('src')).toBe(after);
  });

  it('renders a highlighted class when the highlighted input is true', async () => {
    await renderCard({ card: ASSO, highlighted: true });
    expect(screen.getByTestId('card').classList.contains('highlighted')).toBe(true);
  });
});
