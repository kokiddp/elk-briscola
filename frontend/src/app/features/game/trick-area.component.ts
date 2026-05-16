import { Component, input } from '@angular/core';
import { CardComponent } from './card.component';
import { PlayedCard } from './game.models';

@Component({
  selector: 'bri-trick-area',
  standalone: true,
  imports: [CardComponent],
  templateUrl: './trick-area.component.html',
  styleUrl: './trick-area.component.scss',
})
export class TrickAreaComponent {
  readonly plays = input<readonly PlayedCard[]>([]);

  trackBySeat(_index: number, play: PlayedCard): number {
    return play.seatIndex;
  }
}
