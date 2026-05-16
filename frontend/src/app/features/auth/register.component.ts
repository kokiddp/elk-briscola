import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';
import {
  displayNameValidator,
  passwordValidator,
  usernameValidator,
} from './auth-validators';

@Component({
  selector: 'bri-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, I18nPipe],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    username: ['', [Validators.required, usernameValidator]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, passwordValidator]],
    displayName: ['', [displayNameValidator]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set(null);
    const { username, email, password, displayName } = this.form.getRawValue();
    try {
      await this.auth.register({
        username,
        email,
        password,
        displayName: displayName === '' ? null : displayName,
      });
      await this.auth.login({ usernameOrEmail: username, password });
      await this.router.navigateByUrl('/home');
    } catch (err: unknown) {
      this.errorMessage.set(this.extractErrorMessage(err));
    } finally {
      this.submitting.set(false);
    }
  }

  private extractErrorMessage(err: unknown): string {
    if (typeof err === 'object' && err !== null) {
      const e = err as {
        error?: { code?: string; errors?: { description?: string }[] };
      };
      if (e.error?.errors && e.error.errors.length > 0) {
        return e.error.errors
          .map((x) => x.description ?? '')
          .filter(Boolean)
          .join(' ');
      }
      if (e.error?.code) return e.error.code;
    }
    return this.i18n.t('auth.register.failed');
  }
}
