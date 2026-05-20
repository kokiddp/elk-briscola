import { Component, computed, effect, inject, input } from '@angular/core';
import { ClockService } from '../../core/clock.service';
import { I18nPipe } from '../../shared/i18n.pipe';

@Component({
  selector: 'bri-reconnect-banner',
  standalone: true,
  imports: [I18nPipe],
  templateUrl: './reconnect-banner.component.html',
  styleUrl: './reconnect-banner.component.scss',
})
export class ReconnectBannerComponent {
  readonly deadline = input<Date | null>(null);
  readonly seatIndex = input<number | null>(null);

  private readonly clock = inject(ClockService);

  readonly secondsRemaining = computed<number>(() => {
    const dl = this.deadline();
    if (!dl) {
      return 0;
    }
    const ms = dl.getTime() - this.clock.now();
    return Math.max(0, Math.ceil(ms / 1000));
  });

  readonly visible = computed(() => {
    const dl = this.deadline();
    return dl !== null && dl.getTime() > this.clock.now();
  });

  /** Render-safe seat number: -1 stands in for "no seat" so the template
   * param type stays `number`. The template only renders this when
   * seatIndex() is non-null. */
  readonly seatLabel = computed<number>(() => this.seatIndex() ?? -1);

  constructor() {
    // Share the app-wide clock instead of running our own setInterval
    // (M6). One subscriber here counts toward the shared 1Hz timer.
    effect((onCleanup) => {
      if (!this.deadline()) {
        return;
      }
      const unsub = this.clock.subscribe();
      onCleanup(unsub);
    });
  }
}
