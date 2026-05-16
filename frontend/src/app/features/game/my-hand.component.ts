import { Component, computed, input, output } from '@angular/core';
import { CardComponent } from './card.component';
import { Card, cardKey } from './game.models';

@Component({
  selector: 'bri-my-hand',
  standalone: true,
  imports: [CardComponent],
  templateUrl: './my-hand.component.html',
  styleUrl: './my-hand.component.scss',
})
export class MyHandComponent {
  readonly cards = input<readonly Card[]>([]);
  readonly legalMoves = input<ReadonlySet<string>>(new Set<string>());
  readonly myTurn = input(false);

  readonly cardPlayed = output<Card>();

  readonly disabled = computed(() => !this.myTurn() || this.legalMoves().size === 0);

  isPlayable(card: Card): boolean {
    return this.myTurn() && this.legalMoves().has(cardKey(card));
  }

  onCardClick(card: Card): void {
    if (!this.isPlayable(card)) {
      return;
    }
    this.cardPlayed.emit(card);
  }

  trackByCard(_index: number, card: Card): string {
    return cardKey(card);
  }
}
