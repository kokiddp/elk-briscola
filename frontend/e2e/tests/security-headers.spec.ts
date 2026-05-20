import { expect, test } from '@playwright/test';

/**
 * Regression test for audit H12. The frontend nginx serves the SPA's
 * index.html directly (not via the backend SecurityHeadersMiddleware),
 * so the headers must be set at the edge or the / route ships with
 * no protection. Verify every response that runs JavaScript carries
 * the headers — both the bare / and a deep-link route that falls
 * back through `try_files` to index.html.
 */
test.describe('security headers', () => {
  test('GET / carries the full security-header suite', async ({ request }) => {
    const res = await request.get('/');
    expect(res.status()).toBe(200);
    const h = res.headers();
    expect(h['x-content-type-options']).toBe('nosniff');
    expect(h['x-frame-options']).toBe('DENY');
    expect(h['referrer-policy']).toBe('strict-origin-when-cross-origin');
    expect(h['cross-origin-opener-policy']).toBe('same-origin');
    expect(h['permissions-policy']).toContain('camera=()');
    expect(h['content-security-policy']).toContain("default-src 'self'");
    expect(h['content-security-policy']).toContain('frame-ancestors');
  });

  test('SPA deep-link routes carry the same headers (try_files → index.html)', async ({
    request,
  }) => {
    // /lobby is a client-side route — nginx serves index.html via the
    // try_files fallback. The previous H12 state stripped headers here
    // because the `location = /index.html` block defined its own
    // add_header (which suppressed inheritance).
    const res = await request.get('/lobby');
    expect(res.status()).toBe(200);
    const h = res.headers();
    expect(h['content-security-policy']).toContain("default-src 'self'");
    expect(h['x-frame-options']).toBe('DENY');
  });
});
