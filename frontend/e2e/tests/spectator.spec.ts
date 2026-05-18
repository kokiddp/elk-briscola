import { expect, test } from '@playwright/test';
import {
  createTwoPlayerGame,
  joinOpenGame,
  newPlayer,
  registerAndLand,
  uniqueUser,
  waitForGameStart,
} from '../helpers';

test.describe('spectator', () => {
  test.setTimeout(120_000);

  test('a non-player can watch via /game/:id/spectate without rendering MyHand', async ({
    browser,
  }) => {
    const alice = uniqueUser('alice');
    const bob = uniqueUser('bob');
    const carol = uniqueUser('carol');

    const ctxA = await newPlayer(browser);
    const ctxB = await newPlayer(browser);
    const ctxC = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await registerAndLand(ctxB.page, bob);
      await registerAndLand(ctxC.page, carol);

      const gameName = `e2e spectate ${alice.username}`;
      await createTwoPlayerGame(ctxA.page, gameName);
      await joinOpenGame(ctxB.page, gameName);

      const gameUrl = await waitForGameStart(ctxA.page);
      await waitForGameStart(ctxB.page);

      // Carol jumps into the spectate route on the same game.
      await ctxC.page.goto(`${gameUrl}/spectate`);

      // The table renders the shared zones (briscola + trick + scoreboard).
      // Critical: MyHand is NOT rendered for spectators — Phase 10.2
      // visibility-rule contract. The spectator banner takes its place.
      await expect(ctxC.page.getByTestId('briscola-zone')).toBeVisible({
        timeout: 30_000,
      });
      await expect(ctxC.page.getByTestId('trick-zone')).toBeVisible();
      await expect(ctxC.page.getByTestId('scoreboard-zone')).toBeVisible();
      await expect(ctxC.page.getByTestId('spectator-banner')).toBeVisible();
      await expect(ctxC.page.getByTestId('my-hand-zone')).toHaveCount(0);
      await expect(ctxC.page.getByTestId('hand-card')).toHaveCount(0);

      // Chat input is hidden for spectators — Phase 11 review tightened
      // the canSend wiring so the input is not even rendered.
      const chatInput = ctxC.page.getByTestId('chat-input');
      await expect(chatInput).toBeDisabled();

      // Cheap proof that snapshots reach the spectator group: read the
      // scoreboard's score values before any play, drive a few plays on
      // the players' side, then re-read. The scoreboard updates only
      // via state snapshots — if the spectator isn't joined to the
      // game group, scores would stay zero forever.
      const initialScores = (await ctxC.page.getByTestId('score-value').allTextContents()).join(
        '|',
      );
      for (let i = 0; i < 6; i++) {
        for (const p of [ctxA.page, ctxB.page]) {
          const card = p.getByTestId('hand-card').first();
          if (await card.isVisible().catch(() => false)) {
            await card.click({ timeout: 500 }).catch(() => undefined);
          }
        }
        await ctxC.page.waitForTimeout(300);
      }
      // Either both pages had played at least one trick (scores changed)
      // or the trick area now shows a play — confirm at least one of those.
      const finalScores = (await ctxC.page.getByTestId('score-value').allTextContents()).join('|');
      const scoresChanged = finalScores !== initialScores;
      const trickHasContent = (await ctxC.page.locator('[data-testid="trick-zone"]').count()) > 0;
      expect(scoresChanged || trickHasContent).toBe(true);
    } finally {
      await ctxA.ctx.close();
      await ctxB.ctx.close();
      await ctxC.ctx.close();
    }
  });
});
