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
  // Fallback for older test environments without crypto.randomUUID.
  // Build each segment one hex nibble at a time — `Math.floor(rand * 16^n)`
  // only spans 2^53 of safe-int space, so a 12-nibble call (16^12 = 2^48)
  // is still inside the safe range but a 16-nibble one would alias. Per-
  // nibble construction is uniform across the full hex space regardless
  // of segment length and matches the production crypto.randomUUID branch's
  // observability properties (collision probability is what the log
  // correlation cares about).
  const nibble = (): string => Math.floor(Math.random() * 16).toString(16);
  const seg = (n: number): string => Array.from({ length: n }, nibble).join('');
  return `${seg(8)}-${seg(4)}-${seg(4)}-${seg(4)}-${seg(12)}`;
}
