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
 * Verifies QoL D + E:
 * - lobby open-game card shows the seated player's display name + Elo
 * - the in-game opponent area shows the opponent's name + Elo
 * - the end-game dialog labels seats with names and includes Elo next
 *   to each row.
 */
test.describe('seat info', () => {
  test.setTimeout(180_000);

  test('names + Elos render in the lobby, on the table, and in the end-game dialog', async ({
    browser,
  }) => {
    const alice = uniqueUser('alice');
    const bob = uniqueUser('bob');

    const ctxA = await newPlayer(browser);
    const ctxB = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await registerAndLand(ctxB.page, bob);

      // Alice creates a 2p game. We tab back to bob's /lobby to see the
      // open card BEFORE he joins — that's when the seat-player chip
      // for alice should be visible.
      await createTwoPlayerGame(ctxA.page);

      // Bob refreshes his lobby and finds the row Alice just created
      // (latest open row — backend orders by CreatedAt ASC). The chip
      // for her seat carries her display name + the starting Elo.
      await ctxB.page.goto('/lobby');
      await expect(ctxB.page.getByTestId('open-list')).toBeVisible();
      const aliceRow = ctxB.page.locator('[data-testid="open-list"] li').last();
      const aliceChip = aliceRow.getByTestId('seat-player');
      await expect(aliceChip).toBeVisible({ timeout: 10_000 });
      await expect(aliceChip).toContainText(alice.displayName);
      await expect(aliceChip).toContainText('1500');

      // Now bob joins. Both players auto-route into the game table.
      await joinOpenGame(ctxB.page);
      await waitForGameStart(ctxA.page);
      await waitForGameStart(ctxB.page);

      // The opponent area shows the OTHER player's name + Elo.
      // Alice's table sees bob; bob's table sees alice.
      await expect(ctxA.page.getByTestId('opponent-name')).toContainText(bob.displayName);
      await expect(ctxA.page.getByTestId('opponent-elo')).toContainText('1500');
      await expect(ctxB.page.getByTestId('opponent-name')).toContainText(alice.displayName);
      await expect(ctxB.page.getByTestId('opponent-elo')).toContainText('1500');

      // Play to completion.
      await playToCompletion([ctxA.page, ctxB.page]);
      await expect(ctxA.page.getByTestId('end-game-dialog')).toBeVisible({ timeout: 30_000 });
      await expect(ctxB.page.getByTestId('end-game-dialog')).toBeVisible({ timeout: 30_000 });

      // The end-game score table labels each row with the seat's
      // player name, and the post-game Elo sits next to it. The
      // ranking push has already fired (it lands before GameFinished),
      // so the Elo isn't 1500 anymore — at least one of the two rows
      // shows a value != 1500.
      const rowsA = ctxA.page.getByTestId('end-score-row');
      await expect(rowsA).toHaveCount(2);
      const labelText = (await rowsA.first().textContent()) + (await rowsA.last().textContent());
      expect(labelText).toContain(alice.displayName);
      expect(labelText).toContain(bob.displayName);
      // The Elo values (in parens) include numbers other than 1500.
      const numericValues = labelText.match(/\(([0-9]+)\)/g) ?? [];
      expect(numericValues.length).toBe(2);
      const eloValues = numericValues.map((m) => parseInt(m.slice(1, -1), 10));
      expect(eloValues.every((v) => v > 0)).toBe(true);
      expect(eloValues[0] + eloValues[1]).toBe(3000); // zero-sum at equal seeds
      expect(eloValues[0]).not.toBe(eloValues[1]);
    } finally {
      await ctxA.ctx.close();
      await ctxB.ctx.close();
    }
  });
});
