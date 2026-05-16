import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const AUTH_FREE_PATHS = ['/api/v1/auth/login', '/api/v1/auth/register', '/api/v1/auth/refresh'];

export const httpTokenInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  if (AUTH_FREE_PATHS.some((p) => req.url.includes(p))) {
    return next(req);
  }

  const token = auth.getAccessToken();
  const authed = token ? withToken(req, token) : req;

  return next(authed).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        auth.hasUsableRefreshToken() &&
        !req.headers.has('X-Retry-After-Refresh')
      ) {
        return retryAfterRefresh(req, next, auth);
      }
      return throwError(() => err);
    }),
  );
};

function withToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
}

function retryAfterRefresh(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  auth: AuthService,
): Observable<HttpEvent<unknown>> {
  return from(auth.refresh()).pipe(
    switchMap((token) => {
      if (!token) {
        return throwError(() => new Error('Authentication refresh failed'));
      }
      const retried = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`,
          'X-Retry-After-Refresh': '1',
        },
      });
      return next(retried);
    }),
  );
}
