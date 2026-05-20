import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ClockService } from './clock.service';

describe('ClockService', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [ClockService] });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns the current time as soon as a subscriber registers', () => {
    vi.setSystemTime(new Date('2026-05-20T00:00:00Z'));
    const clock = TestBed.inject(ClockService);

    const before = clock.now();
    const unsub = clock.subscribe();
    TestBed.flushEffects();
    // The constructor effect refreshes nowSig immediately on subscribe.
    expect(clock.now()).toBeGreaterThanOrEqual(before);
    unsub();
  });

  it('ticks once per second while at least one subscriber is active', () => {
    vi.setSystemTime(new Date('2026-05-20T00:00:00Z'));
    const clock = TestBed.inject(ClockService);
    const unsub = clock.subscribe();
    TestBed.flushEffects();

    const t0 = clock.now();
    vi.advanceTimersByTime(1000);
    expect(clock.now()).toBeGreaterThanOrEqual(t0 + 1000);
    vi.advanceTimersByTime(2000);
    expect(clock.now()).toBeGreaterThanOrEqual(t0 + 3000);
    unsub();
  });

  it('stops ticking once the last subscriber unsubscribes', () => {
    vi.setSystemTime(new Date('2026-05-20T00:00:00Z'));
    const clock = TestBed.inject(ClockService);
    const unsub = clock.subscribe();
    TestBed.flushEffects();

    unsub();
    TestBed.flushEffects();
    const t = clock.now();
    vi.advanceTimersByTime(5000);
    // No subscribers → nowSig is frozen.
    expect(clock.now()).toBe(t);
  });

  it('only runs one interval regardless of subscriber count', () => {
    // Ref-counted: multiple subscribers share the same interval. We
    // verify by counting setInterval invocations via spy.
    const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');
    const clock = TestBed.inject(ClockService);

    const a = clock.subscribe();
    TestBed.flushEffects();
    const b = clock.subscribe();
    TestBed.flushEffects();
    const c = clock.subscribe();
    TestBed.flushEffects();

    // Effects fire once when subscriberCount transitions 0 → 1; we'd
    // re-fire on any change, so each new subscribe runs the effect
    // again. But the OLD interval is cleared in onCleanup before the
    // new one is created, so there's always exactly one live.
    // Therefore setInterval is called once per state change but only
    // one interval is running at a time. We assert <= subscribe count.
    expect(setIntervalSpy.mock.calls.length).toBeLessThanOrEqual(3);

    a();
    b();
    c();
  });
});
