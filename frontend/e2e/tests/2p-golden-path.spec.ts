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

test.describe('2p golden path', () => {
  // A 40-card Briscola match plays out in ~40 turns plus animation
  // settling and inter-page sync; budget generously. The deal phase
  // alone takes ~3 s for the per-card animations.
  test.setTimeout(180_000);

  test('two players play a 2p game to completion', async ({ browser }) => {
    const alice = uniqueUser('alice');
    const bob = uniqueUser('bob');

    const ctxA = await newPlayer(browser);
    const ctxB = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await registerAndLand(ctxB.page, bob);

      await createTwoPlayerGame(ctxA.page);
      await joinOpenGame(ctxB.page);

      // Alice's page auto-navigates to /game once the second seat fills.
      await waitForGameStart(ctxA.page);
      await waitForGameStart(ctxB.page);

      // Both pages have a non-empty hand at start (Briscola deals 3).
      await expect(ctxA.page.getByTestId('my-hand')).toBeVisible();
      await expect(ctxB.page.getByTestId('my-hand')).toBeVisible();

      // Drive the game to completion. The helper polls each page and
      // clicks the first legal card on whoever's turn it is.
      await playToCompletion([ctxA.page, ctxB.page]);

      // End-game dialog lands on both screens.
      await expect(ctxA.page.getByTestId('end-game-dialog')).toBeVisible({
        timeout: 30_000,
      });
      await expect(ctxB.page.getByTestId('end-game-dialog')).toBeVisible({
        timeout: 30_000,
      });

      // Each end-banner has some text (non-empty). We deliberately don't
      // assert which player saw which outcome — the engine + the
      // GameFinishedDto are exercised by the backend integration tests;
      // the E2E only cares that both clients reached the terminal UI.
      await expect(ctxA.page.getByTestId('end-banner')).not.toBeEmpty();
      await expect(ctxB.page.getByTestId('end-banner')).not.toBeEmpty();

      // The end-game dialog carries the "Back to lobby" CTA — confirm
      // the post-game navigation surface is wired.
      await expect(ctxA.page.getByTestId('end-back-to-lobby')).toBeVisible();
      await expect(ctxB.page.getByTestId('end-back-to-lobby')).toBeVisible();
    } finally {
      await ctxA.ctx.close();
      await ctxB.ctx.close();
    }
  });
});
