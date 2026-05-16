import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, lastValueFrom } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from './auth.service';
import { httpTokenInterceptor } from './http-token.interceptor';

interface AuthStubOpts {
  accessToken?: string | null;
  hasRefresh?: boolean;
  refreshResult?: string | null;
}

function authStub(opts: AuthStubOpts = {}) {
  const refresh = vi.fn(() => Promise.resolve(opts.refreshResult ?? null));
  const stub = {
    getAccessToken: () => opts.accessToken ?? null,
    hasUsableRefreshToken: () => opts.hasRefresh ?? false,
    refresh,
  };
  return { stub, refresh };
}

function setup(opts: AuthStubOpts = {}) {
  const { stub, refresh } = authStub(opts);
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: stub },
      provideHttpClient(withInterceptors([httpTokenInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  return {
    http: TestBed.inject(HttpClient),
    ctrl: TestBed.inject(HttpTestingController),
    refresh,
  };
}

describe('httpTokenInterceptor', () => {
  beforeEach(() => {
    // ensure clean module each test
  });

  it('attaches Bearer Authorization when a token is present', async () => {
    const { http, ctrl } = setup({ accessToken: 't-1' });
    const p = firstValueFrom(http.get('/api/v1/games'));
    const req = ctrl.expectOne('/api/v1/games');
    expect(req.request.headers.get('Authorization')).toBe('Bearer t-1');
    req.flush({});
    await p;
    ctrl.verify();
  });

  it('does not attach a header when no token is present', async () => {
    const { http, ctrl } = setup();
    const p = firstValueFrom(http.get('/api/v1/games'));
    const req = ctrl.expectOne('/api/v1/games');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
    await p;
    ctrl.verify();
  });

  it('skips token attachment for auth-free paths', async () => {
    const { http, ctrl } = setup({ accessToken: 't-1' });
    const p = firstValueFrom(http.post('/api/v1/auth/login', {}));
    const req = ctrl.expectOne('/api/v1/auth/login');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
    await p;
    ctrl.verify();
  });

  it('refreshes and retries once on a 401 when a refresh token is usable', async () => {
    const { http, ctrl, refresh } = setup({
      accessToken: 't-1',
      hasRefresh: true,
      refreshResult: 't-2',
    });
    const p = lastValueFrom(http.get('/api/v1/me'));
    const first = ctrl.expectOne('/api/v1/me');
    expect(first.request.headers.get('Authorization')).toBe('Bearer t-1');
    first.flush({ code: 'expired' }, { status: 401, statusText: 'Unauthorized' });

    // The retry happens asynchronously after refresh() resolves.
    await Promise.resolve();
    const second = ctrl.expectOne('/api/v1/me');
    expect(second.request.headers.get('Authorization')).toBe('Bearer t-2');
    expect(second.request.headers.get('X-Retry-After-Refresh')).toBe('1');
    second.flush({ ok: true });

    expect(await p).toEqual({ ok: true });
    expect(refresh).toHaveBeenCalledTimes(1);
    ctrl.verify();
  });

  it('does not retry a 401 when no usable refresh token exists', async () => {
    const { http, ctrl, refresh } = setup({ accessToken: 't-1' });
    const p = firstValueFrom(http.get('/api/v1/me'));
    ctrl.expectOne('/api/v1/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await expect(p).rejects.toMatchObject({ status: 401 });
    expect(refresh).not.toHaveBeenCalled();
    ctrl.verify();
  });

  it('does not retry a 401 a second time (no infinite loop)', async () => {
    const { http, ctrl } = setup({
      accessToken: 't-1',
      hasRefresh: true,
      refreshResult: 't-2',
    });
    const p = firstValueFrom(http.get('/api/v1/me'));
    ctrl.expectOne('/api/v1/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    ctrl.expectOne('/api/v1/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await expect(p).rejects.toMatchObject({ status: 401 });
    ctrl.verify();
  });

  it('surfaces an error when refresh returns null', async () => {
    const { http, ctrl } = setup({
      accessToken: 't-1',
      hasRefresh: true,
      refreshResult: null,
    });
    const p = firstValueFrom(http.get('/api/v1/me'));
    ctrl.expectOne('/api/v1/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await expect(p).rejects.toBeInstanceOf(Error);
    ctrl.verify();
  });
});
