import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';

@Component({
  selector: 'bri-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, I18nPipe],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

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
      // Users want to play, not read the splash — drop them straight
      // into the lobby once they're authenticated.
      await this.router.navigateByUrl('/lobby');
    } catch (err: unknown) {
      this.errorMessage.set(this.extractErrorMessage(err));
    } finally {
      this.submitting.set(false);
    }
  }

  private extractErrorMessage(err: unknown): string {
    if (typeof err === 'object' && err !== null) {
      const e = err as { status?: number; error?: { code?: string } };
      if (e.status === 401) {
        return this.i18n.t('auth.login.invalidCredentials');
      }
      if (e.error?.code) return e.error.code;
    }
    return this.i18n.t('auth.login.failed');
  }
}
