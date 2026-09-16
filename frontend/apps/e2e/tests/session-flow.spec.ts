import { test, expect } from '@playwright/test';
import {
  createGameAsAdmin,
  isFullStackAvailable,
  joinGameViaTile,
} from './helpers.js';

test.describe('session multi-game', () => {
  test('late joiner can enter between games before next start', async ({ browser, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    const adminContext = await browser.newContext();
    const player2Context = await browser.newContext();
    const player3Context = await browser.newContext();
    const lateContext = await browser.newContext();

    const adminPage = await adminContext.newPage();
    const player2Page = await player2Context.newPage();
    const player3Page = await player3Context.newPage();
    const latePage = await lateContext.newPage();

    const sessionCode = await createGameAsAdmin(adminPage);
    await joinGameViaTile(player2Page, sessionCode);
    await joinGameViaTile(player3Page, sessionCode);

    // Between games (no active round yet): late joiner may enter the lobby.
    await joinGameViaTile(latePage, sessionCode);

    await adminPage.waitForFunction(
      () => {
        const items = document.querySelector('word-game-widget')?.shadowRoot?.querySelectorAll('.wg-member-list li');
        return (items?.length ?? 0) >= 4;
      },
      { timeout: 60_000 },
    );

    const startButton = adminPage.locator('word-game-widget').locator('[data-action="start"]');
    await expect(startButton).toBeEnabled({ timeout: 30_000 });
    await startButton.click();

    await adminPage.waitForFunction(
      () => {
        const root = document.querySelector('word-game-widget')?.shadowRoot;
        const word = root?.querySelector('.wg-word');
        const status = root?.textContent ?? '';
        return (word?.textContent?.trim().length ?? 0) > 0 || status.includes('IN_PROGRESS');
      },
      { timeout: 120_000 },
    );

    // Late joiner should be in the started game roster (snapshotted at start).
    await latePage.waitForFunction(
      () => {
        const root = document.querySelector('word-game-widget')?.shadowRoot;
        const word = root?.querySelector('.wg-word');
        const members = root?.querySelectorAll('.wg-member-list li')?.length ?? 0;
        return ((word?.textContent?.trim().length ?? 0) > 0 || (root?.textContent ?? '').includes('IN_PROGRESS'))
          && members >= 4;
      },
      { timeout: 120_000 },
    );

    await adminContext.close();
    await player2Context.close();
    await player3Context.close();
    await lateContext.close();
  });
});
