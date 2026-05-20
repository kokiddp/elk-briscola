import { Component, inject, input, output } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { I18nPipe } from '../../shared/i18n.pipe';

/**
 * Profile-page form for changing the authenticated user's password.
 * Server enforces the policy too; the form-side validators are pre-
 * flight UX (minLength 10 + at least one letter + at least one digit
 * mirror Briscola.Infrastructure.DependencyInjection's Identity setup).
 */
@Component({
  selector: 'bri-change-password-form',
  standalone: true,
  imports: [ReactiveFormsModule, I18nPipe],
  templateUrl: './change-password-form.component.html',
})
export class ChangePasswordFormComponent {
  private readonly fb = inject(FormBuilder);

  readonly submitting = input(false);
  readonly submitted = output<{ currentPassword: string; newPassword: string }>();

  readonly form = this.fb.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    newPassword: [
      '',
      [
        Validators.required,
        Validators.minLength(10),
        ChangePasswordFormComponent.hasLetter,
        ChangePasswordFormComponent.hasDigit,
      ],
    ],
    confirmPassword: ['', [Validators.required]],
  });

  // Cross-field validator: newPassword === confirmPassword.
  constructor() {
    this.form.controls.confirmPassword.addValidators((c) => {
      const np = this.form?.controls?.newPassword?.value;
      return c.value && np && c.value !== np ? { mismatch: true } : null;
    });
    // Re-validate confirm when newPassword changes.
    this.form.controls.newPassword.valueChanges.subscribe(() => {
      this.form.controls.confirmPassword.updateValueAndValidity({ emitEvent: false });
    });
  }

  onSubmit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.submitted.emit({ currentPassword: v.currentPassword, newPassword: v.newPassword });
  }

  private static hasLetter(c: AbstractControl): ValidationErrors | null {
    return /[A-Za-z]/.test(c.value ?? '') ? null : { noLetter: true };
  }
  private static hasDigit(c: AbstractControl): ValidationErrors | null {
    return /\d/.test(c.value ?? '') ? null : { noDigit: true };
  }
}
