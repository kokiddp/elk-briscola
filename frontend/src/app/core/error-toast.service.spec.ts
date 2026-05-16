import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorToastService } from './error-toast.service';

describe('ErrorToastService', () => {
  let service: ErrorToastService;

  beforeEach(() => {
    service = new ErrorToastService();
    vi.useFakeTimers();
  });

  it('pushes a toast with auto-incrementing ids', () => {
    const a = service.error('boom');
    const b = service.info('hello');
    expect(b).toBe(a + 1);
    expect(service.toasts()).toHaveLength(2);
    expect(service.toasts()[0]?.kind).toBe('error');
    expect(service.toasts()[1]?.kind).toBe('info');
  });

  it('dismisses by id', () => {
    const id = service.success('ok');
    service.dismiss(id);
    expect(service.toasts()).toHaveLength(0);
  });

  it('auto-dismisses after the configured duration', () => {
    service.show('error', 'boom', 1_000);
    expect(service.toasts()).toHaveLength(1);
    vi.advanceTimersByTime(1_000);
    expect(service.toasts()).toHaveLength(0);
  });
});
