import { Component, computed, input } from '@angular/core';
import { I18nPipe } from '../../shared/i18n.pipe';
import { CardComponent } from './card.component';

@Component({
  selector: 'bri-opponent-area',
  standalone: true,
  imports: [CardComponent, I18nPipe],
  templateUrl: './opponent-area.component.html',
  styleUrl: './opponent-area.component.scss',
})
export class OpponentAreaComponent {
  readonly seatIndex = input.required<number>();
  readonly cardCount = input.required<number>();
  readonly displayName = input<string>('');
  readonly elo = input<number | null>(null);
  readonly isPartner = input(false);
  readonly isActive = input(false);
  readonly isDisconnected = input(false);

  /** A fixed-size array used purely for *@for rendering N face-down cards. */
  readonly stack = computed<number[]>(() => {
    const n = Math.max(0, this.cardCount());
    return Array.from({ length: n }, (_, i) => i);
  });
}
