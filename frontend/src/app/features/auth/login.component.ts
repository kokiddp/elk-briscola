import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'bri-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    usernameOrEmail: ['', [Validators.required]],
    password: ['', [Validators.required]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set(null);
    try {
      await this.auth.login(this.form.getRawValue());
      await this.router.navigateByUrl('/home');
    } catch (err: unknown) {
      this.errorMessage.set(extractErrorMessage(err) ?? 'Login failed.');
    } finally {
      this.submitting.set(false);
    }
  }
}

function extractErrorMessage(err: unknown): string | null {
  if (typeof err === 'object' && err !== null) {
    const e = err as { status?: number; error?: { code?: string } };
    if (e.status === 401) return 'Invalid username/email or password.';
    if (e.error?.code) return e.error.code;
  }
  return null;
}
