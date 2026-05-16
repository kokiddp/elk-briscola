import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import {
  displayNameValidator,
  passwordValidator,
  usernameValidator,
} from './auth-validators';

@Component({
  selector: 'bri-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

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
      this.errorMessage.set(extractErrorMessage(err) ?? 'Registration failed.');
    } finally {
      this.submitting.set(false);
    }
  }
}

function extractErrorMessage(err: unknown): string | null {
  if (typeof err === 'object' && err !== null) {
    const e = err as { status?: number; error?: { code?: string; errors?: { description?: string }[] } };
    if (e.error?.errors && e.error.errors.length > 0) {
      return e.error.errors.map((x) => x.description ?? '').filter(Boolean).join(' ');
    }
    if (e.error?.code) return e.error.code;
  }
  return null;
}
