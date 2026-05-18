import { expect, test } from '@playwright/test';
import {
  createTwoPlayerGame,
  joinOpenGame,
  newPlayer,
  registerAndLand,
  uniqueUser,
  waitForGameStart,
} from '../helpers';

test.describe('pending-game UX', () => {
  test.setTimeout(120_000);

  test('creator can cancel their own pending game', async ({ browser }) => {
    const alice = uniqueUser('alice');
    const ctxA = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await createTwoPlayerGame(ctxA.page);

      // Pending banner visible + cancel button rendered.
      await expect(ctxA.page.getByTestId('pending-banner')).toBeVisible();
      const cancelBtn = ctxA.page.getByTestId('cancel-pending');
      await expect(cancelBtn).toBeVisible();

      // Click cancel → banner disappears + create-game button re-enabled.
      await cancelBtn.click();
      await expect(ctxA.page.getByTestId('pending-banner')).toHaveCount(0, {
        timeout: 10_000,
      });
      await expect(ctxA.page.getByTestId('create-game')).toBeEnabled();
    } finally {
      await ctxA.ctx.close();
    }
  });

  test('refresh preserves the pending state — creator still sees the banner + cannot join their own game', async ({
    browser,
  }) => {
    const alice = uniqueUser('alice');
    const ctxA = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await createTwoPlayerGame(ctxA.page);
      await expect(ctxA.page.getByTestId('pending-banner')).toBeVisible();

      // Hard refresh.
      await ctxA.page.reload();

      // After the refresh + re-auth + re-fetch of openGames, the
      // pending banner is back, our own row shows the passive
      // "Waiting…" label (no Join button on it), and the create-game
      // button is disabled. Other rows in the open list (leftover from
      // prior runs) may still have a Join button but it must be
      // disabled (hasPendingGame gate).
      await expect(ctxA.page.getByTestId('pending-banner')).toBeVisible({
        timeout: 15_000,
      });
      await expect(ctxA.page.getByTestId('waiting-label')).toBeVisible();
      await expect(ctxA.page.getByTestId('create-game')).toBeDisabled();
      const joinButtons = ctxA.page.getByTestId('join-button');
      const joinCount = await joinButtons.count();
      for (let i = 0; i < joinCount; i++) {
        await expect(joinButtons.nth(i)).toBeDisabled();
      }
    } finally {
      await ctxA.ctx.close();
    }
  });

  test('creator on /profile is auto-routed into the game when the table fills', async ({
    browser,
  }) => {
    const alice = uniqueUser('alice');
    const bob = uniqueUser('bob');

    const ctxA = await newPlayer(browser);
    const ctxB = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await registerAndLand(ctxB.page, bob);

      await createTwoPlayerGame(ctxA.page);
      // Make sure Alice's seat shows up in the lobby state before she
      // wanders off — that's when the LobbyService captures her
      // pendingGameId. If she navigated before the seatPlayers signal
      // landed, the auto-route wouldn't know which game to look for.
      await expect(ctxA.page.getByTestId('waiting-label')).toBeVisible({ timeout: 10_000 });

      // Alice navigates away — to /profile — while waiting.
      await ctxA.page.goto('/profile');
      await expect(ctxA.page).toHaveURL(/\/profile$/);

      // Bob joins from his own lobby. The server emits gameStarted to
      // the lobby:open group, which Alice's hub connection is still in.
      await joinOpenGame(ctxB.page);
      await waitForGameStart(ctxB.page);

      // Alice's tab auto-navigated away from /profile to the game URL —
      // the LobbyService's global effect handles it from any route.
      await expect(ctxA.page).toHaveURL(/\/game\/[0-9a-f-]+$/, { timeout: 30_000 });
      await expect(ctxA.page.getByTestId('my-hand')).toBeVisible({ timeout: 30_000 });
    } finally {
      await ctxA.ctx.close();
      await ctxB.ctx.close();
    }
  });
});
