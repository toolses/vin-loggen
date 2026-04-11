import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright e2e configuration.
 *
 * The dev server is started automatically before the test run.
 * Set PLAYWRIGHT_BASE_URL to override (useful in CI with a pre-deployed URL).
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries:   process.env['CI'] ? 2 : 0,
  workers:   process.env['CI'] ? 1 : undefined,
  reporter:  'html',

  use: {
    baseURL:       process.env['PLAYWRIGHT_BASE_URL'] ?? 'http://localhost:4200',
    trace:         'on-first-retry',
    screenshot:    'only-on-failure',
  },

  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],

  // Start the Angular dev server automatically when running locally.
  // In CI, set PLAYWRIGHT_BASE_URL to the deployed URL instead.
  webServer: process.env['PLAYWRIGHT_BASE_URL'] ? undefined : {
    command: 'npm run start',
    url:     'http://localhost:4200',
    reuseExistingServer: !process.env['CI'],
    timeout: 120_000,
  },
});
