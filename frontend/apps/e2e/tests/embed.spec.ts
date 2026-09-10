import { test, expect } from '@playwright/test';
import { createGameAsAdmin, isFullStackAvailable } from './helpers.js';

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

    // Create room (Admin tab) exercises auth + session events.
    await createGameAsAdmin(page);

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
