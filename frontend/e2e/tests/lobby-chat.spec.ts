import { expect, test } from '@playwright/test';
import { newPlayer, registerAndLand, uniqueUser } from '../helpers';

test.describe('lobby chat', () => {
  test.setTimeout(60_000);

  test('two users in the lobby see each other’s chat in real time', async ({ browser }) => {
    const alice = uniqueUser('alice');
    const bob = uniqueUser('bob');

    const ctxA = await newPlayer(browser);
    const ctxB = await newPlayer(browser);
    try {
      await registerAndLand(ctxA.page, alice);
      await registerAndLand(ctxB.page, bob);

      await ctxA.page.goto('/lobby');
      await ctxB.page.goto('/lobby');

      // Each page mounts the chat panel after the LobbyHub negotiation
      // lands; wait for the input to be ready before typing.
      await expect(ctxA.page.getByTestId('chat-input')).toBeVisible();
      await expect(ctxB.page.getByTestId('chat-input')).toBeVisible();

      const message = `hello-${Date.now().toString(36)}`;
      await ctxA.page.getByTestId('chat-input').fill(message);
      await ctxA.page.getByTestId('chat-send').click();

      // Both pages render the same message in their chat log via the
      // SignalR fan-out. The signal travels A → server → both groups.
      await expect(ctxA.page.getByTestId('chat-message').last()).toContainText(message, {
        timeout: 10_000,
      });
      await expect(ctxB.page.getByTestId('chat-message').last()).toContainText(message, {
        timeout: 10_000,
      });
    } finally {
      await ctxA.ctx.close();
      await ctxB.ctx.close();
    }
  });
});
