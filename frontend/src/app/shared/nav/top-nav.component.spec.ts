import { signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth.service';
import { LobbyService } from '../../features/lobby/lobby.service';
import { TopNavComponent } from './top-nav.component';

/**
 * Coverage for the top-nav. Audit L1 flagged this and game-table-page
 * as the two component files without specs; this one covers the
 * resume-pill logic + the auth-gated visibility branch.
 */
describe('TopNavComponent', () => {
  async function setup(opts: { authed?: boolean; runningGameId?: string | null }) {
    const userSig = signal<{ id: string; username: string; displayName: string } | null>(
      opts.authed ? { id: 'u', username: 'alice', displayName: 'Alice' } : null,
    );
    const runningSig = signal<string | null>(opts.runningGameId ?? null);

    const auth = {
      isAuthenticated: () => userSig() !== null,
      currentUser: userSig,
      logout: vi.fn(() => Promise.resolve()),
    } as unknown as AuthService;

    const lobby = {
      currentRunningGameId: runningSig,
    } as unknown as LobbyService;

    const r = await render(TopNavComponent, {
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: LobbyService, useValue: lobby },
      ],
    });
    const router = r.fixture.debugElement.injector.get(Router);
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    return { ...r, userSig, runningSig, auth, router, navigateByUrl };
  }

  it('does not render the nav when the user is unauthenticated', async () => {
    await setup({ authed: false });
    expect(screen.queryByTestId('nav-lobby')).toBeNull();
    expect(screen.queryByTestId('nav-profile')).toBeNull();
    expect(screen.queryByTestId('nav-logout')).toBeNull();
  });

  it('renders Lobby / Profile / Logout when authenticated', async () => {
    await setup({ authed: true });
    expect(screen.getByTestId('nav-lobby')).toBeInTheDocument();
    expect(screen.getByTestId('nav-profile')).toBeInTheDocument();
    expect(screen.getByTestId('nav-logout')).toBeInTheDocument();
  });

  it('hides the resume-game pill when no running game is in flight', async () => {
    await setup({ authed: true, runningGameId: null });
    expect(screen.queryByTestId('nav-resume-game')).toBeNull();
  });

  it('renders the resume-game pill pointing at /game/:id when a running game is in flight', async () => {
    const gid = '00000000-0000-0000-0000-000000000abc';
    await setup({ authed: true, runningGameId: gid });
    const pill = screen.getByTestId('nav-resume-game');
    expect(pill).toBeInTheDocument();
    expect(pill.getAttribute('href')).toBe(`/game/${gid}`);
  });

  it('shows the display name fallback chain (display name → username → empty)', async () => {
    const { fixture } = await setup({ authed: true });
    expect(screen.getByTestId('nav-user').textContent?.trim()).toBe('Alice');
    fixture.destroy();
  });
});
