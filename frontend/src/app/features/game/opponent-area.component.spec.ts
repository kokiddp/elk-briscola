import { render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { CardSetService } from '../../card-sets/card-set.service';
import { Card } from './game.models';
import { OpponentAreaComponent } from './opponent-area.component';

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
    seatIndex?: number;
    cardCount?: number;
    displayName?: string;
    isPartner?: boolean;
    isActive?: boolean;
    isDisconnected?: boolean;
  } = {},
) {
  return render(OpponentAreaComponent, {
    providers: [{ provide: CardSetService, useValue: cardSetsStub() }],
    inputs: {
      seatIndex: opts.seatIndex ?? 1,
      cardCount: opts.cardCount ?? 3,
      displayName: opts.displayName ?? '',
      isPartner: opts.isPartner ?? false,
      isActive: opts.isActive ?? false,
      isDisconnected: opts.isDisconnected ?? false,
    },
  });
}

describe('OpponentAreaComponent', () => {
  it('renders cardCount face-down cards', async () => {
    await setup({ cardCount: 3 });
    const stack = screen.getByTestId('opponent-stack');
    expect(stack.querySelectorAll('bri-card').length).toBe(3);
    expect(screen.getByTestId('card-count')).toHaveTextContent('3');
  });

  it('renders zero cards when cardCount is zero', async () => {
    await setup({ cardCount: 0 });
    expect(screen.getByTestId('opponent-stack').querySelectorAll('bri-card').length).toBe(0);
    expect(screen.getByTestId('card-count')).toHaveTextContent('0');
  });

  it('shows a partner chip in 4p mode', async () => {
    await setup({ isPartner: true });
    expect(screen.getByTestId('partner-chip')).toBeInTheDocument();
  });

  it('omits the partner chip by default', async () => {
    await setup({ isPartner: false });
    expect(screen.queryByTestId('partner-chip')).toBeNull();
  });

  it('falls back to the seat label when displayName is empty', async () => {
    await setup({ seatIndex: 2, displayName: '' });
    expect(screen.getByTestId('opponent-name')).toHaveTextContent(/seat 2/i);
  });

  it('uses the supplied displayName when present', async () => {
    await setup({ displayName: 'Bob' });
    expect(screen.getByTestId('opponent-name')).toHaveTextContent('Bob');
  });

  it('flags the disconnected state on the wrapper', async () => {
    await setup({ isDisconnected: true });
    expect(screen.getByTestId('opponent-area').classList.contains('disconnected')).toBe(true);
  });
});
