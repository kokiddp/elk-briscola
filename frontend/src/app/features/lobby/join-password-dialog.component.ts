import { Component, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { I18nPipe } from '../../shared/i18n.pipe';

/**
 * Modal that asks for a private-game password before joining. Replaces
 * the previous `window.prompt(...)` call in `LobbyComponent.onJoin` —
 * the audit's H7. `prompt` blocks the JS thread, can't be styled, is
 * untestable in Playwright reliably, and is deprecated in cross-origin
 * iframes.
 */
@Component({
  selector: 'bri-join-password-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, I18nPipe],
  template: `
    <div
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-4"
      data-testid="join-password-backdrop"
    >
      <section
        class="flex w-full max-w-sm flex-col gap-3 rounded-2xl bg-white p-6 text-stone-900 shadow-2xl"
        role="dialog"
        aria-modal="true"
        data-testid="join-password-dialog"
      >
        <h2 class="m-0 font-display text-xl font-semibold">
          {{ 'lobby.join.passwordPrompt' | t }}
        </h2>
        <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate class="flex flex-col gap-3">
          <input
            type="password"
            formControlName="password"
            autocomplete="current-password"
            data-testid="join-password-input"
            class="rounded-md border border-stone-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/30"
            autofocus
          />
          <div class="flex justify-end gap-2">
            <button
              type="button"
              (click)="onCancel()"
              [disabled]="submitting()"
              data-testid="join-password-cancel"
              class="rounded-md border border-stone-300 bg-white px-3 py-1.5 text-sm font-medium text-stone-700 transition hover:bg-stone-100 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {{ 'lobby.create.cancel' | t }}
            </button>
            <button
              type="submit"
              [disabled]="submitting() || form.invalid"
              data-testid="join-password-submit"
              class="rounded-md bg-brand-600 px-3 py-1.5 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              @if (submitting()) {
                {{ 'lobby.joining' | t }}
              } @else {
                {{ 'lobby.join' | t }}
              }
            </button>
          </div>
        </form>
      </section>
    </div>
  `,
})
export class JoinPasswordDialogComponent {
  private readonly fb = inject(FormBuilder);

  readonly submitting = input(false);
  readonly submitted = output<string>();
  readonly cancelled = output();

  readonly form = this.fb.nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(1)]],
  });

  onSubmit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitted.emit(this.form.getRawValue().password);
  }

  onCancel(): void {
    if (this.submitting()) {
      return;
    }
    this.cancelled.emit();
  }
}
