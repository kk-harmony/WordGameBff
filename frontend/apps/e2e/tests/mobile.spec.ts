import { test, expect } from '@playwright/test';

test.describe('mobile viewport', () => {
  test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

  test('home tiles render on iPhone viewport', async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('word-game-widget', { timeout: 60_000 });

    const widget = page.locator('word-game-widget');
    await expect(widget.locator('[data-action="start-game"]')).toBeVisible({ timeout: 30_000 });
    await expect(widget.locator('[data-action="show-join"]')).toBeVisible();
  });
});
