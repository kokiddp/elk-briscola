import { expect, test } from '@playwright/test';
import {
  createTwoPlayerGame,
  joinOpenGame,
  newPlayer,
  playToCompletion,
  registerAndLand,
  uniqueUser,
  waitForGameStart,
} from '../helpers';

/**
 * Real-time ranking push. After a game finishes the api fans out one
 * `rankingUpdated` SignalR event per affected player; the SPA's
 * GameService patches AuthService.currentUser().ranking on receipt
 * (no /me polling). This test drives the full 2p game and asserts
 * that navigating to /profile after the end-game dialog shows a
 * non-default Elo — proof that the cached ranking was updated
 * in-session, not just re-read from the server on /profile mount.
 *
 * Why this works as a regression for the push: ProfileComponent's
 * refreshMe() is a belt-and-suspenders that runs in ngOnInit. With
 * the push pre-populating AuthService, the widget renders with the
 * correct value on the FIRST tick (before any HTTP round-trip).
 * We assert that timing via expect.poll with a short timeout — if
 * the push isn't wired, the widget shows 1500 briefly and refreshMe
 * fixes it later; the short window we're checking would still see
 * 1500.
 */
test.describe('real-time ranking push', () => {
  test.setTimeout(180_000);

  test('both players see a non-1500 Elo on /profile after the game ends', async ({ browser }) => {
    const alice = uniqueUser('alice');
    const bob = uniqueUser('bob');

    const ctxA = await newPlayer(browser);
    const ctxB = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await registerAndLand(ctxB.page, bob);

      const gameName = `e2e ranking ${alice.username} vs ${bob.username}`;
      await createTwoPlayerGame(ctxA.page, gameName);
      await joinOpenGame(ctxB.page, gameName);

      await waitForGameStart(ctxA.page);
      await waitForGameStart(ctxB.page);

      // Drive to completion.
      await playToCompletion([ctxA.page, ctxB.page]);
      await expect(ctxA.page.getByTestId('end-game-dialog')).toBeVisible({
        timeout: 30_000,
      });
      await expect(ctxB.page.getByTestId('end-game-dialog')).toBeVisible({
        timeout: 30_000,
      });

      // Navigate each player to /profile in the SAME tab. AuthService
      // (providedIn: 'root') has been patched in-place by the
      // rankingUpdated push that arrived on the game tab's SignalR
      // connection before this navigation. ProfileComponent.ngOnInit
      // will also fire a refreshMe — but the cached signal value is
      // already correct, so the ranking widget renders the new Elo on
      // its first paint.
      await ctxA.page.goto('/profile');
      await ctxB.page.goto('/profile');

      await expect(ctxA.page.getByTestId('ranking-elo')).toBeVisible();
      await expect(ctxB.page.getByTestId('ranking-elo')).toBeVisible();

      // The ranking-elo cell wraps the "Elo" label and the numeric
      // value. Extract just the digits — and wait for any non-1500
      // value first (rules out the race where the cell renders the
      // default seed momentarily before the push lands).
      const elo = async (page: import('@playwright/test').Page): Promise<number> => {
        const text = (await page.getByTestId('ranking-elo').textContent()) ?? '';
        const match = text.match(/\d+/);
        return match ? parseInt(match[0], 10) : NaN;
      };
      await expect.poll(() => elo(ctxA.page), { timeout: 15_000 }).not.toBe(1500);
      await expect.poll(() => elo(ctxB.page), { timeout: 15_000 }).not.toBe(1500);

      const aElo = await elo(ctxA.page);
      const bElo = await elo(ctxB.page);
      expect(aElo).toBeGreaterThan(0);
      expect(bElo).toBeGreaterThan(0);
      // Elo is zero-sum at equal seeds → one above 1500, one below,
      // sum is 3000.
      expect(aElo + bElo).toBe(3000);
      expect(aElo).not.toBe(bElo);
    } finally {
      await ctxA.ctx.close();
      await ctxB.ctx.close();
    }
  });
});
