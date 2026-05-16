import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { correlationIdInterceptor } from './correlation-id.interceptor';

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([correlationIdInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  return {
    http: TestBed.inject(HttpClient),
    ctrl: TestBed.inject(HttpTestingController),
  };
}

describe('correlationIdInterceptor', () => {
  it('attaches a UUID-shaped X-Correlation-Id when none is set', async () => {
    const { http, ctrl } = setup();
    const p = firstValueFrom(http.get('/api/v1/anything'));
    const req = ctrl.expectOne('/api/v1/anything');
    const id = req.request.headers.get('X-Correlation-Id');
    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    req.flush({});
    await p;
    ctrl.verify();
  });

  it('preserves a caller-supplied X-Correlation-Id header', async () => {
    const { http, ctrl } = setup();
    const p = firstValueFrom(
      http.get('/api/v1/anything', { headers: { 'X-Correlation-Id': 'caller-id-42' } }),
    );
    const req = ctrl.expectOne('/api/v1/anything');
    expect(req.request.headers.get('X-Correlation-Id')).toBe('caller-id-42');
    req.flush({});
    await p;
    ctrl.verify();
  });

  it('produces a different id per request', async () => {
    const { http, ctrl } = setup();
    const seen = new Set<string>();
    for (let i = 0; i < 3; i++) {
      const p = firstValueFrom(http.get(`/api/v1/r/${i}`));
      const req = ctrl.expectOne(`/api/v1/r/${i}`);
      seen.add(req.request.headers.get('X-Correlation-Id') ?? '');
      req.flush({});
      await p;
    }
    expect(seen.size).toBe(3);
    ctrl.verify();
  });
});
