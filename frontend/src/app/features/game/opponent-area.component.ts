import { Component, computed, effect, inject, input } from '@angular/core';
import { ClockService } from '../../core/clock.service';
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

  private readonly clock = inject(ClockService);
  readonly idleSecondsRemaining = computed<number | null>(() => {
    const dl = this.idleDeadline();
    if (!dl) {
      return null;
    }
    const ms = dl.getTime() - this.clock.now();
    return Math.max(0, Math.ceil(ms / 1000));
  });

  constructor() {
    // Subscribe to the shared ClockService while a deadline is active.
    // Each instance refs the clock once and unsubs when the deadline
    // clears or the component is destroyed; the service maintains a
    // single 1-second interval shared across all consumers (M6).
    effect((onCleanup) => {
      if (!this.idleDeadline()) {
        return;
      }
      const unsub = this.clock.subscribe();
      onCleanup(unsub);
    });
  }
}
