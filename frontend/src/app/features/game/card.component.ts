import { Component, computed, inject, input } from '@angular/core';
import { CardSetService } from '../../card-sets/card-set.service';
import { Card, cardKey } from './game.models';

@Component({
  selector: 'bri-card',
  standalone: true,
  imports: [],
  templateUrl: './card.component.html',
  styleUrl: './card.component.scss',
})
export class CardComponent {
  private readonly cardSets = inject(CardSetService);

  readonly card = input<Card | null>(null);
  readonly alt = input<string>('');
  readonly highlighted = input(false);

  readonly src = computed<string>(() => {
    const c = this.card();
    const set = this.cardSets.activeSet();
    return c ? set.resolveFront(c) : set.resolveBack();
  });

  readonly altText = computed<string>(() => {
    const explicit = this.alt();
    if (explicit) {
      return explicit;
    }
    const c = this.card();
    return c ? `${c.rank} di ${c.suit}` : 'Card back';
  });

  /**
   * Image-load fallback: if the active set is missing this specific asset,
   * swap to the placeholder set's URL. The browser's caching means the
   * fallback URL only loads once per (set, card) pair.
   */
  onImageError(event: Event): void {
    const img = event.target as HTMLImageElement;
    const c = this.card();
    const setId = this.cardSets.activeSetId();
    const fallback = c
      ? this.cardSets.fallbackFrontUrl(c, setId)
      : this.cardSets.fallbackBackUrl(setId);
    if (img.src.endsWith(fallback)) {
      // Already showing the fallback; avoid a loop if even the placeholder
      // is missing (which would mean a deployment problem, not an asset
      // problem).
      return;
    }
    img.src = fallback;
  }

  trackByKey(): string {
    const c = this.card();
    return c ? cardKey(c) : 'back';
  }
}
