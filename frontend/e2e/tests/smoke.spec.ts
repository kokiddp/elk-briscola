import { expect, test } from '@playwright/test';
import { newPlayer, registerAndLand, uniqueUser } from '../helpers';

/**
 * Liveness smoke: the compose stack is reachable, a fresh user can
 * register, and the post-register landing renders. Catches breakages
 * in the nginx ingress, the api routing, or the SPA bootstrap before
 * the heavier 2p / reconnect tests run.
 */
test('register + land on /lobby', async ({ browser }) => {
  const { ctx, page } = await newPlayer(browser);
  try {
    await registerAndLand(page, uniqueUser('smoke'));
    await expect(page).toHaveURL(/\/lobby$/);
    // The create-game CTA proves the lobby UI mounted + the LobbyHub
    // negotiation completed.
    await expect(page.getByTestId('create-game')).toBeVisible();
  } finally {
    await ctx.close();
  }
});
