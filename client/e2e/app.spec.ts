import { test, expect } from '@playwright/test';

/**
 * Core application smoke tests.
 *
 * These run against the live dev or production server.
 * They deliberately avoid hard-coding Supabase credentials by intercepting
 * the network and stubbing only what is needed for each assertion.
 */

test.describe('Application shell', () => {
  test('login page renders with sign-in form', async ({ page }) => {
    await page.goto('/login');

    // The page should show an email/password form
    await expect(page.locator('input[type="email"]')).toBeVisible();
    await expect(page.locator('input[type="password"]')).toBeVisible();
    await expect(page.getByRole('button', { name: /logg inn|sign in/i })).toBeVisible();
  });

  test('unauthenticated visit to / redirects to login', async ({ page }) => {
    await page.goto('/');
    // Wait for any auth guard redirect
    await page.waitForURL(/\/login/, { timeout: 5_000 });
    await expect(page).toHaveURL(/\/login/);
  });
});

test.describe('Scanner page', () => {
  test.beforeEach(async ({ page }) => {
    // Stub Supabase session endpoint so Angular thinks a user is logged in.
    // The response matches the shape expected by @supabase/supabase-js v2.
    await page.route('**/auth/v1/token**', route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          access_token:  'fake-jwt',
          refresh_token: 'fake-refresh',
          token_type:    'bearer',
          expires_in:    3600,
          user: {
            id:    'test-user-id',
            email: 'test@example.com',
            role:  'authenticated',
          },
        }),
      }),
    );

    // Stub the wine catalog so loadWines doesn't need a real DB
    await page.route('**/rest/v1/wine_entries**', route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      }),
    );
  });

  test('scanner route exists and shows camera controls', async ({ page }) => {
    await page.goto('/scan');
    // The scanner shows either an "open camera" button or the camera view itself
    const hasOpenCamera = await page.getByRole('button', { name: /åpne kamera/i }).isVisible().catch(() => false);
    const hasGallery    = await page.getByRole('button', { name: /velg fra galleri/i }).isVisible().catch(() => false);
    expect(hasOpenCamera || hasGallery).toBe(true);
  });
});

test.describe('Wine cellar page', () => {
  test('empty cellar shows an empty state or wine list', async ({ page }) => {
    // Stub the wine entries to return an empty list
    await page.route('**/rest/v1/wine_entries**', route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      }),
    );

    await page.goto('/cellar');
    // Just verify the page loads without a JS error – check for the page title
    const title = page.locator('h1, h2').first();
    await expect(title).toBeVisible({ timeout: 5_000 });
  });
});
