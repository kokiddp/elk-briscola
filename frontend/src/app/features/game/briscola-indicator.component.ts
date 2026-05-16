import { Component, computed, input } from '@angular/core';
import { CardComponent } from './card.component';
import { Card } from './game.models';

@Component({
  selector: 'bri-briscola-indicator',
  standalone: true,
  imports: [CardComponent],
  templateUrl: './briscola-indicator.component.html',
  styleUrl: './briscola-indicator.component.scss',
})
export class BriscolaIndicatorComponent {
  readonly briscolaCard = input.required<Card>();
  readonly stockCount = input.required<number>();

  /**
   * The briscola card sits perpendicular under the stock until the stock
   * empties; at that point the engine has already dealt it (the leading
   * player gets the briscola as their last draw), so we fade it out.
   */
  readonly visible = computed(() => this.stockCount() > 0);
}
