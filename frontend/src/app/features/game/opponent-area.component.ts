import { Component, computed, effect, input, signal } from '@angular/core';
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
  /** Forfeit-by-idle deadline for this seat. The server only emits the
   *  warning once the player has been thinking past Game:IdleWarnSeconds,
   *  so the countdown surfaces only inside the final auto-resign window. */
  readonly idleDeadline = input<Date | null>(null);

  /** A fixed-size array used purely for *@for rendering N face-down cards. */
  readonly stack = computed<number[]>(() => {
    const n = Math.max(0, this.cardCount());
    return Array.from({ length: n }, (_, i) => i);
  });

  private readonly nowSig = signal(Date.now());
  readonly idleSecondsRemaining = computed<number | null>(() => {
    const dl = this.idleDeadline();
    if (!dl) {
      return null;
    }
    const ms = dl.getTime() - this.nowSig();
    return Math.max(0, Math.ceil(ms / 1000));
  });

  constructor() {
    effect((onCleanup) => {
      if (!this.idleDeadline()) {
        return;
      }
      const id = setInterval(() => this.nowSig.set(Date.now()), 1000);
      onCleanup(() => clearInterval(id));
    });
  }
}
