import { Component, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { I18nPipe } from '../../shared/i18n.pipe';

/**
 * Profile-page form for changing the authenticated user's email.
 * Requires the current password as confirmation — the server demands
 * it too, but the form-side validator catches the empty case before
 * the round-trip.
 */
@Component({
  selector: 'bri-change-email-form',
  standalone: true,
  imports: [ReactiveFormsModule, I18nPipe],
  templateUrl: './change-email-form.component.html',
})
export class ChangeEmailFormComponent {
  private readonly fb = inject(FormBuilder);

  readonly currentEmail = input<string>('');
  readonly submitting = input(false);
  readonly submitted = output<{ currentPassword: string; newEmail: string }>();

  readonly form = this.fb.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    newEmail: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
  });

  onSubmit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitted.emit(this.form.getRawValue());
    // Don't reset; parent flips submitting + the toast covers UX.
    // On success the parent navigates away and the form unmounts.
  }
}
