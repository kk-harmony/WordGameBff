import { test, expect } from '@playwright/test';
import { isFullStackAvailable } from './helpers.js';

test.describe('embed widget', () => {
  test('loads via script tag and completes PoW auth', async ({ page, request }) => {
    page.on('console', (msg) => {
      const text = msg.text();
      expect(text).not.toMatch(/sessionToken|Bearer /i);
    });

    await page.addInitScript(() => {
      window.addEventListener('wordgame:ready', () => {
        (window as unknown as { __ready?: boolean }).__ready = true;
      });
      window.addEventListener('wordgame:session', ((e: CustomEvent) => {
        (window as unknown as { __sessions: unknown[] }).__sessions =
          (window as unknown as { __sessions?: unknown[] }).__sessions ?? [];
        (window as unknown as { __sessions: unknown[] }).__sessions.push(e.detail);
      }) as EventListener);
    });

    await page.goto('/?debug=1');
    await page.waitForFunction(() => (window as unknown as { WordGame?: { version: string } }).WordGame?.version);
    await page.waitForSelector('word-game-widget', { timeout: 60_000 });

    await page.waitForFunction(() => (window as unknown as { __ready?: boolean }).__ready === true, {
      timeout: 30_000,
    });

    await expect(page.locator('word-game-widget').locator('[data-action="start-game"]')).toBeVisible({
      timeout: 30_000,
    });

    const sessionsBefore = await page.evaluate(
      () => (window as unknown as { __sessions?: unknown[] }).__sessions?.length ?? 0,
    );
    expect(sessionsBefore).toBe(0);

    await page.locator('word-game-widget').locator('[data-action="start-game"]').click();

    await page.waitForFunction(
      () => ((window as unknown as { __sessions?: unknown[] }).__sessions?.length ?? 0) > 0,
      { timeout: 120_000 },
    );

    const version = await page.evaluate(() => (window as unknown as { WordGame: { version: string } }).WordGame.version);
    expect(version).toMatch(/^\d+\.\d+\.\d+$/);

    const sessions = await page.evaluate(() => (window as unknown as { __sessions?: unknown[] }).__sessions ?? []);
    expect(sessions.length).toBeGreaterThan(0);
    const session = sessions[0] as { userId?: string; sessionToken?: string };
    expect(session.userId).toBeTruthy();
    expect(session.sessionToken).toBeUndefined();

    if (await isFullStackAvailable(request)) {
      await page.waitForFunction(
        () => {
          const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
          return value?.textContent && /^\d+$/.test(value.textContent.trim());
        },
        { timeout: 120_000 },
      );
    }
  });
});
