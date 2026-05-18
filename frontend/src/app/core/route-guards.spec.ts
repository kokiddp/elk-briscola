import { Injector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from './auth.service';
import { authGuard, guestGuard } from './route-guards';

function makeAuth(opts: {
  isAuthenticated?: boolean;
  hasUsableRefreshToken?: boolean;
  refreshResult?: string | null;
}): AuthService {
  const stub = {
    isAuthenticated: () => opts.isAuthenticated ?? false,
    hasUsableRefreshToken: () => opts.hasUsableRefreshToken ?? false,
    refresh: () => Promise.resolve(opts.refreshResult ?? null),
  };
  return stub as unknown as AuthService;
}

async function run<T>(injector: Injector, fn: () => T | Promise<T>): Promise<T> {
  return await runInInjectionContext(injector, fn);
}

describe('authGuard', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    router = {
      parseUrl: (s: string) => ({ tag: 'urltree', s }) as unknown as UrlTree,
    } as Router;
  });

  it('allows when already authenticated', async () => {
    const injector = Injector.create({
      providers: [
        { provide: AuthService, useValue: makeAuth({ isAuthenticated: true }) },
        { provide: Router, useValue: router },
      ],
    });
    const result = await run(injector, () => authGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('attempts a refresh when a refresh token is available, then allows', async () => {
    let isAuthed = false;
    const auth = {
      isAuthenticated: () => isAuthed,
      hasUsableRefreshToken: () => true,
      refresh: async () => {
        isAuthed = true;
        return 'new-access';
      },
    } as unknown as AuthService;
    const injector = Injector.create({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router },
      ],
    });
    const result = await run(injector, () => authGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('redirects to /login when not authenticated and no refresh token', async () => {
    const injector = Injector.create({
      providers: [
        { provide: AuthService, useValue: makeAuth({}) },
        { provide: Router, useValue: router },
      ],
    });
    const result = await run(injector, () => authGuard({} as never, {} as never));
    expect(result).toEqual(expect.objectContaining({ tag: 'urltree', s: '/login' }));
  });
});

describe('guestGuard', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    router = {
      parseUrl: (s: string) => ({ tag: 'urltree', s }) as unknown as UrlTree,
    } as Router;
  });

  it('allows when not authenticated', async () => {
    const injector = Injector.create({
      providers: [
        { provide: AuthService, useValue: makeAuth({}) },
        { provide: Router, useValue: router },
      ],
    });
    const result = await run(injector, () => guestGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('redirects authenticated callers to /lobby', async () => {
    const injector = Injector.create({
      providers: [
        { provide: AuthService, useValue: makeAuth({ isAuthenticated: true }) },
        { provide: Router, useValue: router },
      ],
    });
    const result = await run(injector, () => guestGuard({} as never, {} as never));
    expect(result).toEqual(expect.objectContaining({ tag: 'urltree', s: '/lobby' }));
  });
});
