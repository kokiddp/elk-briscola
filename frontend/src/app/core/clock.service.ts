import { Injectable, Signal, computed, effect, signal } from '@angular/core';

/**
 * App-wide 1-second clock. Components that render time-based UI
 * (countdown chips, deadline banners, last-seen timestamps) read
 * `now` instead of running their own `setInterval` so we don't end up
 * with N redundant timers firing the change detector once per second.
 *
 * Activation is reference-counted via `subscribe()` — the interval
 * only runs while at least one consumer has an active subscription,
 * so the service is idle on /lobby and /profile (no countdowns).
 *
 * Audit M6.
 */
@Injectable({ providedIn: 'root' })
export class ClockService {
  private readonly nowSig = signal(Date.now());
  private readonly subscriberCountSig = signal(0);

  /** Current epoch milliseconds, updated once per second while at
   *  least one subscriber is active. */
  readonly now: Signal<number> = computed(() => this.nowSig());

  constructor() {
    // Start / stop the interval based on subscriber count.
    effect((onCleanup) => {
      const active = this.subscriberCountSig() > 0;
      if (!active) {
        return;
      }
      // Refresh immediately so a consumer that just subscribed gets
      // a fresh `now` without waiting up to 1 second.
      this.nowSig.set(Date.now());
      const handle = setInterval(() => this.nowSig.set(Date.now()), 1000);
      onCleanup(() => clearInterval(handle));
    });
  }

  /**
   * Reference-count one consumer. Returns an unsubscribe function the
   * caller must invoke when their countdown ends (or via an effect's
   * onCleanup). The interval stops as soon as the count drops to zero.
   */
  subscribe(): () => void {
    this.subscriberCountSig.update((n) => n + 1);
    let unsubscribed = false;
    return () => {
      if (unsubscribed) return;
      unsubscribed = true;
      this.subscriberCountSig.update((n) => Math.max(0, n - 1));
    };
  }
}
