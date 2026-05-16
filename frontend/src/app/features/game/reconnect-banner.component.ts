import {
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { I18nPipe } from '../../shared/i18n.pipe';

@Component({
  selector: 'bri-reconnect-banner',
  standalone: true,
  imports: [I18nPipe],
  templateUrl: './reconnect-banner.component.html',
  styleUrl: './reconnect-banner.component.scss',
})
export class ReconnectBannerComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  readonly deadline = input<Date | null>(null);
  readonly seatIndex = input<number | null>(null);

  private readonly nowSig = signal(Date.now());

  readonly secondsRemaining = computed<number>(() => {
    const dl = this.deadline();
    if (!dl) {
      return 0;
    }
    const ms = dl.getTime() - this.nowSig();
    return Math.max(0, Math.ceil(ms / 1000));
  });

  readonly visible = computed(() => {
    const dl = this.deadline();
    return dl !== null && dl.getTime() > this.nowSig();
  });

  /** Render-safe seat number: -1 stands in for "no seat" so the template
   * param type stays `number`. The template only renders this when
   * seatIndex() is non-null. */
  readonly seatLabel = computed<number>(() => this.seatIndex() ?? -1);

  constructor() {
    // Pause the tick when nothing is being counted down.
    effect((onCleanup) => {
      const dl = this.deadline();
      if (!dl) {
        return;
      }
      const id = setInterval(() => this.nowSig.set(Date.now()), 1000);
      onCleanup(() => clearInterval(id));
    });
  }

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => undefined);
  }
}
