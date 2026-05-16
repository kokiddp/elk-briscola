import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nPipe } from '../../shared/i18n.pipe';

@Component({
  selector: 'bri-game-placeholder',
  standalone: true,
  imports: [RouterLink, I18nPipe],
  template: `
    <section class="game-placeholder">
      <h1>{{ 'game.placeholder.title' | t }}</h1>
      <p>{{ 'game.placeholder.body' | t }}</p>
      <p data-testid="game-id">{{ gameId() }}</p>
      <a routerLink="/lobby">{{ 'game.placeholder.back' | t }}</a>
    </section>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .game-placeholder {
        max-width: 32rem;
        margin: 4rem auto;
        text-align: center;
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
      }
    `,
  ],
})
export class GamePlaceholderComponent {
  private readonly route = inject(ActivatedRoute);

  gameId(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }
}
