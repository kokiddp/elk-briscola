import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }
  if (auth.hasUsableRefreshToken()) {
    const token = await auth.refresh();
    if (token && auth.isAuthenticated()) {
      return true;
    }
  }
  return router.parseUrl('/login');
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  // Authenticated users hitting /login or /register are bounced to the
  // lobby — same destination as the login form's post-submit nav, so
  // there's no "land on /home, blink, redirect to /lobby" jitter.
  return auth.isAuthenticated() ? router.parseUrl('/lobby') : true;
};
