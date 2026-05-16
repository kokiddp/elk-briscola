import { Component, computed, input } from '@angular/core';
import { CardComponent } from './card.component';

const VISIBLE_STACK_MAX = 5;

@Component({
  selector: 'bri-stock',
  standalone: true,
  imports: [CardComponent],
  templateUrl: './stock.component.html',
  styleUrl: './stock.component.scss',
})
export class StockComponent {
  readonly count = input.required<number>();

  /**
   * Rendering more than ~5 stacked card backs adds no clarity and burns
   * paint cost. Cap the visible stack and rely on the count badge for the
   * exact size.
   */
  readonly visibleStack = computed<number[]>(() => {
    const n = Math.min(Math.max(0, this.count()), VISIBLE_STACK_MAX);
    return Array.from({ length: n }, (_, i) => i);
  });

  readonly isEmpty = computed(() => this.count() <= 0);
}
