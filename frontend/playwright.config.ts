import { defineConfig, devices } from '@playwright/test';

/**
 * The Phase 13 E2E suite drives the full compose stack — Postgres + api +
 * frontend (nginx) — at http://localhost:8080. We deliberately don't
 * launch `ng serve`: prod-shape ingress is what we want to keep regression-
 * protected, including the nginx reverse-proxy contract.
 *
 * For local runs:
 *   docker compose up -d --build
 *   ( cd frontend && npx playwright test )
 *
 * CI brings the stack up + tears it down via .github/workflows/e2e.yml.
 */
export default defineConfig({
  testDir: './e2e',
  // E2E specs run their own flows end-to-end and shouldn't share state
  // across files; per-file isolation is fine and lets failure repros stay
  // self-contained.
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  // One retry on CI for flaky network-layer hiccups; never locally so
  // genuine flakes surface during development.
  retries: process.env.CI ? 1 : 0,
  // Two workers max — the suite runs two browser contexts per test (the
  // two players); more parallelism just contends for the api's auth
  // rate-limit window.
  workers: process.env.CI ? 1 : 2,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
  ],
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:8080',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    // Each action gets 10 s. Inter-page sync points (waiting for the
    // other player's snapshot) can take longer — those use explicit
    // `expect.poll` / `waitFor` and override individually.
    actionTimeout: 10_000,
    navigationTimeout: 30_000,
  },
  projects: [
    {
      // Single Chromium project. Each test creates two `browser.newContext()`
      // instances internally — that's how we drive Alice + Bob from one
      // Playwright run rather than via Playwright's project-level matrix.
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
