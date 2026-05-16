import { animate, style, transition, trigger } from '@angular/animations';
import { Component, input } from '@angular/core';
import { CardComponent } from './card.component';
import { PlayedCard } from './game.models';

/**
 * Card fly-in animation. Reduced-motion neutralisation is done at the
 * CSS layer (see trick-area.component.scss): the Angular engine still
 * runs the trigger, but the @media query collapses the duration so the
 * keyframes are effectively a no-op.
 */
export const cardEnterTrigger = trigger('cardEnter', [
  transition(':enter', [
    style({ transform: 'translateY(-50%) scale(0.85)', opacity: 0 }),
    animate('220ms ease-out', style({ transform: 'translateY(0) scale(1)', opacity: 1 })),
  ]),
  transition(':leave', [animate('180ms ease-in', style({ opacity: 0, transform: 'scale(0.9)' }))]),
]);

@Component({
  selector: 'bri-trick-area',
  standalone: true,
  imports: [CardComponent],
  templateUrl: './trick-area.component.html',
  styleUrl: './trick-area.component.scss',
  animations: [cardEnterTrigger],
})
export class TrickAreaComponent {
  readonly plays = input<readonly PlayedCard[]>([]);

  trackBySeat(_index: number, play: PlayedCard): number {
    return play.seatIndex;
  }
}
