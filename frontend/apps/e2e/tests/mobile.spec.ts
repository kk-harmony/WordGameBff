import { test, expect } from '@playwright/test';

test.describe('mobile viewport', () => {
  test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

  test('home player tab is default on iPhone viewport', async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('word-game-widget', { timeout: 60_000 });

    const widget = page.locator('word-game-widget');
    await expect(widget.locator('[data-action="home-tab-player"]')).toBeVisible({ timeout: 30_000 });
    await expect(widget.locator('[data-action="home-tab-player"]')).toHaveAttribute('aria-selected', 'true');
    await expect(widget.locator('#wg-join-id')).toBeVisible();
    await expect(widget.locator('[data-action="start-game"]')).toHaveCount(0);

    await widget.locator('[data-action="home-tab-admin"]').click();
    await expect(widget.locator('[data-action="start-game"]')).toBeVisible();
  });
});
