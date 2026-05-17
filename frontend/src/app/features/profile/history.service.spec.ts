import { HttpResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MatchHistoryPage } from '../../core/models';
import { MatchHistoryService } from './history.service';

describe('MatchHistoryService', () => {
  let svc: MatchHistoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([])),
        provideHttpClientTesting(),
        MatchHistoryService,
      ],
    });
    svc = TestBed.inject(MatchHistoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('GETs /api/v1/me/history with page + size query params', async () => {
    const promise = svc.loadPage(2, 25);
    const req = http.expectOne((r) => r.url === '/api/v1/me/history');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('size')).toBe('25');
    const body: MatchHistoryPage = { items: [], page: 2, pageSize: 25, totalCount: 0 };
    req.flush(body);
    const result = await promise;
    expect(result).toEqual(body);
  });

  it('clamps page and size to a positive integer client-side', async () => {
    const promise = svc.loadPage(0, -5);
    const req = http.expectOne((r) => r.url === '/api/v1/me/history');
    // The server also clamps, but doing it client-side avoids round-trip
    // 400s if the UI ever derives the value from arithmetic that produced 0.
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('1');
    req.flush({ items: [], page: 1, pageSize: 1, totalCount: 0 });
    await promise;
  });

  it('propagates network errors so callers can render an error state', async () => {
    const promise = svc.loadPage(1, 10);
    const req = http.expectOne((r) => r.url === '/api/v1/me/history');
    req.flush('boom', new HttpResponse({ status: 500, statusText: 'Server Error' }));
    await expect(promise).rejects.toBeDefined();
  });
});
