import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { GamePlaceholderComponent } from './game-placeholder.component';

async function setup(id: string) {
  return render(GamePlaceholderComponent, {
    providers: [
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap({ id }) } },
      },
    ],
  });
}

describe('GamePlaceholderComponent', () => {
  it('renders the placeholder copy and a back-to-lobby link', async () => {
    await setup('abc-123');
    expect(screen.getByRole('heading', { name: /game table/i, level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to lobby/i })).toHaveAttribute('href', '/lobby');
  });

  it('surfaces the :id route parameter', async () => {
    await setup('abc-123');
    expect(screen.getByTestId('game-id')).toHaveTextContent('abc-123');
  });

  it('renders an empty id when no param is present', async () => {
    await render(GamePlaceholderComponent, {
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({}) } },
        },
      ],
    });
    expect(screen.getByTestId('game-id').textContent ?? '').toBe('');
  });
});
