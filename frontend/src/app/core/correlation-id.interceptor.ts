import { HttpInterceptorFn } from '@angular/common/http';

export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.headers.has('X-Correlation-Id')) {
    return next(req);
  }
  const correlated = req.clone({
    setHeaders: { 'X-Correlation-Id': generateCorrelationId() },
  });
  return next(correlated);
};

function generateCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  // Fallback that produces a deterministic-shape correlation id when
  // `crypto.randomUUID` is unavailable (older test environments).
  const rand = (n: number): string =>
    Math.floor(Math.random() * Math.pow(16, n))
      .toString(16)
      .padStart(n, '0');
  return `${rand(8)}-${rand(4)}-${rand(4)}-${rand(4)}-${rand(12)}`;
}
