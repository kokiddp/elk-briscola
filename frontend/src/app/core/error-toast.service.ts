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

  readonly toasts = this.toastsSig.asReadonly();

  show(kind: ToastKind, message: string, durationMs: number = DEFAULT_DURATION_MS): number {
    const id = this.nextId++;
    this.toastsSig.update((list) => [...list, { id, kind, message }]);
    if (durationMs > 0) {
      setTimeout(() => this.dismiss(id), durationMs);
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
    this.toastsSig.update((list) => list.filter((t) => t.id !== id));
  }

  clear(): void {
    this.toastsSig.set([]);
  }
}
