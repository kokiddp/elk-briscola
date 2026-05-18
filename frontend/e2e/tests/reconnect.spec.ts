import { expect, test } from '@playwright/test';
import {
  createTwoPlayerGame,
  joinOpenGame,
  loginAndLand,
  newPlayer,
  playToCompletion,
  registerAndLand,
  uniqueUser,
  waitForGameStart,
} from '../helpers';

test.describe('reconnect', () => {
  test.setTimeout(180_000);

  test('Alice survives a full context teardown mid-game and finishes the match', async ({
    browser,
  }) => {
    const alice = uniqueUser('alice');
    const bob = uniqueUser('bob');

    const ctxA1 = await newPlayer(browser);
    const ctxB = await newPlayer(browser);
    let gameUrl: string;
    try {
      await registerAndLand(ctxA1.page, alice);
      await registerAndLand(ctxB.page, bob);

      const gameName = `e2e reconnect ${alice.username} vs ${bob.username}`;
      await createTwoPlayerGame(ctxA1.page, gameName);
      await joinOpenGame(ctxB.page, gameName);

      gameUrl = await waitForGameStart(ctxA1.page);
      await waitForGameStart(ctxB.page);

      // Both hands rendered → mid-game state established.
      await expect(ctxA1.page.getByTestId('my-hand')).toBeVisible();

      // Drive at least one trick so we're genuinely mid-game (not just
      // at deal-finished state). We do this by counting Alice's hand
      // cards before and after — Briscola starts each player with 3,
      // so once she has 2 we know at least one of her plays was
      // accepted (and Bob's response landed in between).
      const initialHand = await ctxA1.page.getByTestId('hand-card').count();
      expect(initialHand).toBe(3);
      await expect
        .poll(
          async () => {
            // Each loop, try playing on either side. Whoever is on turn
            // will succeed; the other gets a silent InvalidMove.
            for (const p of [ctxA1.page, ctxB.page]) {
              const card = p.getByTestId('hand-card').first();
              if (await card.isVisible().catch(() => false)) {
                await card.click({ timeout: 500 }).catch(() => undefined);
              }
            }
            return ctxA1.page.getByTestId('hand-card').count();
          },
          {
            timeout: 30_000,
            intervals: [500, 500, 1_000],
            message: 'expected Alice to play at least one card before teardown',
          },
        )
        .toBeLessThan(initialHand);
    } finally {
      // SCRATCH Alice's context entirely — cookies, localStorage, the
      // SignalR connection, the SPA itself. This is the harshest form of
      // disconnect short of yanking the network adapter.
      await ctxA1.ctx.close();
    }

    // Brief pause so the server has time to register the disconnect and
    // start the reconnect-grace timer. The grace window in Production is
    // 120 s — well under that.
    await new Promise((r) => setTimeout(r, 2_000));

    // Bring Alice back from a clean slate. New context = re-login.
    const ctxA2 = await newPlayer(browser);
    try {
      await loginAndLand(ctxA2.page, alice);

      // Navigate directly to the game URL. The frontend's GameService
      // calls JoinGame(gameId) which the room treats as a reconnect.
      await ctxA2.page.goto(gameUrl);

      // The game table renders again with my-hand populated — server
      // re-pushed the snapshot.
      await expect(ctxA2.page.getByTestId('my-hand')).toBeVisible({
        timeout: 30_000,
      });

      // Continue play to completion. Both Bob's original context and
      // Alice's new context drive the rest of the game.
      await playToCompletion([ctxA2.page, ctxB.page]);

      await expect(ctxA2.page.getByTestId('end-game-dialog')).toBeVisible({
        timeout: 30_000,
      });
      await expect(ctxB.page.getByTestId('end-game-dialog')).toBeVisible({
        timeout: 30_000,
      });
    } finally {
      await ctxA2.ctx.close();
      await ctxB.ctx.close();
    }
  });
});
