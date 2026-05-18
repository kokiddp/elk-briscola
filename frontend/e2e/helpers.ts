import type { Browser, BrowserContext, Page } from '@playwright/test';
import { expect } from '@playwright/test';

/**
 * Shared helpers for the Phase 13 E2E specs. We model each test as two
 * isolated browser contexts (Alice + Bob) running against the same
 * compose stack on http://localhost:8080.
 */

export interface TestUser {
  username: string;
  email: string;
  password: string;
  displayName: string;
}

/**
 * Generates a unique user per test run. The compose stack persists users
 * across runs (via the pgdata volume), so we need entropy: timestamp +
 * Playwright's worker id + a short random suffix.
 */
export function uniqueUser(label: string): TestUser {
  const suffix = `${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`;
  const username = `${label}_${suffix}`.toLowerCase();
  return {
    username,
    email: `${username}@e2e.local`,
    password: 'Strong-Pass-123',
    displayName: label,
  };
}

/** Spawns a fresh browser context + page pair. Caller owns the cleanup. */
export async function newPlayer(browser: Browser): Promise<{ ctx: BrowserContext; page: Page }> {
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  return { ctx, page };
}

/**
 * Logs in an existing user. Lands on /home. Used by the reconnect spec
 * after the original context is torn down.
 */
export async function loginAndLand(page: Page, user: TestUser): Promise<void> {
  await page.goto('/login');
  await page.getByTestId('usernameOrEmail').fill(user.username);
  await page.getByTestId('password').fill(user.password);
  await page.getByTestId('submit').click();
  await expect(page).toHaveURL(/\/(home|lobby)$/, { timeout: 15_000 });
}

/**
 * Registers the user and lands on /home (the post-auth landing route).
 * Reuses the existing register form — we do NOT go through login because
 * register auto-authenticates the new user.
 */
export async function registerAndLand(page: Page, user: TestUser): Promise<void> {
  await page.goto('/register');
  await page.getByTestId('username').fill(user.username);
  await page.getByTestId('email').fill(user.email);
  await page.getByTestId('password').fill(user.password);
  await page.getByTestId('displayName').fill(user.displayName);
  await page.getByTestId('submit').click();
  // The post-register redirect lands on /home for newly-registered users.
  await expect(page).toHaveURL(/\/(home|lobby)$/, { timeout: 15_000 });
}

/**
 * Opens the create-game dialog and submits a 2p game with the given name.
 * Waits for the lobby to redirect into /game/:id (the create flow auto-
 * navigates the creator into the waiting room).
 */
export async function createTwoPlayerGame(page: Page): Promise<void> {
  await page.goto('/lobby');
  await page.getByTestId('create-game').click();
  await page.getByTestId('create-submit').click();
  // The lobby keeps the user on /lobby until a second seat joins; the
  // creator's pending row carries data-testid="pending-banner".
  await expect(page.getByTestId('pending-banner')).toBeVisible({ timeout: 10_000 });
}

/**
 * The second player clicks Join on the (only) open game. Game names
 * are no longer rendered, so we just pick the first row in the open
 * list — tests use isolated, single-game scenarios.
 */
export async function joinOpenGame(page: Page): Promise<void> {
  await page.goto('/lobby');
  await page.locator('[data-testid="open-list"] li').first().getByTestId('join-button').click();
  await expect(page).toHaveURL(/\/game\/[0-9a-f-]+$/, { timeout: 15_000 });
}

/** Waits for Alice's pending-banner to clear (she's been auto-routed to /game). */
export async function waitForGameStart(page: Page): Promise<string> {
  await expect(page).toHaveURL(/\/game\/[0-9a-f-]+$/, { timeout: 30_000 });
  // The connecting placeholder disappears once the first snapshot arrives.
  await expect(page.getByTestId('my-hand')).toBeVisible({ timeout: 30_000 });
  const url = page.url();
  return url.replace(/^.*\/game\//, '/game/');
}

/**
 * Drives both pages alternately until one of them shows the end-game
 * dialog. Strategy: poll every page; if it has any clickable hand-card,
 * click the first one. That's enough because the engine rejects illegal
 * moves with InvalidMove("NotYourTurn") and the player just retries on
 * the next poll.
 */
export async function playToCompletion(
  pages: Page[],
  opts: { maxTurns?: number } = {},
): Promise<void> {
  const maxTurns = opts.maxTurns ?? 200;
  for (let turn = 0; turn < maxTurns; turn++) {
    if (await anyEndDialog(pages)) {
      return;
    }
    for (const p of pages) {
      const card = p.getByTestId('hand-card').first();
      if (await card.isVisible().catch(() => false)) {
        // Best-effort click — if it's not our turn the server NACKs with
        // an InvalidMove and the hand stays as-is. Either way we loop.
        await card.click({ timeout: 500 }).catch(() => undefined);
      }
    }
    await pages[0].waitForTimeout(150);
  }
  throw new Error(`playToCompletion: ${maxTurns} turns without end-game dialog`);
}

async function anyEndDialog(pages: Page[]): Promise<boolean> {
  for (const p of pages) {
    if (
      await p
        .getByTestId('end-game-dialog')
        .isVisible()
        .catch(() => false)
    ) {
      return true;
    }
  }
  return false;
}
