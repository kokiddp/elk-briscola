import { Injectable, signal } from '@angular/core';

export type ToastKind = 'error' | 'info' | 'success';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

const DEFAULT_DURATION_MS = 5_000;

@Injectable({ providedIn: 'root' })
export class ErrorToastService {
  private readonly toastsSig = signal<Toast[]>([]);
  private nextId = 1;
  /** Pending auto-dismiss timers, keyed by toast id. We clear the
   *  setTimeout when the user dismisses a toast manually so a long-
   *  running session doesn't accumulate orphaned timers in the
   *  microtask queue. */
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  readonly toasts = this.toastsSig.asReadonly();

  show(kind: ToastKind, message: string, durationMs: number = DEFAULT_DURATION_MS): number {
    const id = this.nextId++;
    this.toastsSig.update((list) => [...list, { id, kind, message }]);
    if (durationMs > 0) {
      const handle = setTimeout(() => this.dismiss(id), durationMs);
      this.timers.set(id, handle);
    }
    return id;
  }

  error(message: string): number {
    return this.show('error', message);
  }

  info(message: string): number {
    return this.show('info', message);
  }

  success(message: string): number {
    return this.show('success', message);
  }

  dismiss(id: number): void {
    const handle = this.timers.get(id);
    if (handle !== undefined) {
      clearTimeout(handle);
      this.timers.delete(id);
    }
    this.toastsSig.update((list) => list.filter((t) => t.id !== id));
  }

  clear(): void {
    for (const handle of this.timers.values()) {
      clearTimeout(handle);
    }
    this.timers.clear();
    this.toastsSig.set([]);
  }
}
