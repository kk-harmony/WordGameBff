import { test, expect } from '@playwright/test';
import {
  createGameAsAdmin,
  isFullStackAvailable,
  joinGameViaTile,
  waitForHome,
} from './helpers.js';

test.describe('game flow', () => {
  test('three players join waiting room and admin can start', async ({ browser, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');
    const adminContext = await browser.newContext();
    const player2Context = await browser.newContext();
    const player3Context = await browser.newContext();

    const adminPage = await adminContext.newPage();
    const player2Page = await player2Context.newPage();
    const player3Page = await player3Context.newPage();

    const gameId = await createGameAsAdmin(adminPage);

    for (const page of [player2Page, player3Page]) {
      await joinGameViaTile(page, gameId);
    }

    await adminPage.waitForFunction(
      () => {
        const items = document.querySelector('word-game-widget')?.shadowRoot?.querySelectorAll('.wg-member-list li');
        return (items?.length ?? 0) >= 3;
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

    await adminContext.close();
    await player2Context.close();
    await player3Context.close();
  });

  test('admin can abandon accidental create and join another game', async ({ browser, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    const accidentalContext = await browser.newContext();
    const hostContext = await browser.newContext();
    const accidentalPage = await accidentalContext.newPage();
    const hostPage = await hostContext.newPage();

    // Accidental Start traps the player as admin of an empty waiting room.
    await createGameAsAdmin(accidentalPage);
    await expect(
      accidentalPage.locator('word-game-widget').locator('[data-action="leave-waiting"]'),
    ).toBeVisible({ timeout: 30_000 });

    await accidentalPage.locator('word-game-widget').locator('[data-action="leave-waiting"]').click();
    await accidentalPage.waitForFunction(
      () =>
        document.querySelector('word-game-widget')?.shadowRoot?.querySelector('[data-action="home-tab-player"]') != null
        && document.querySelector('word-game-widget')?.shadowRoot?.querySelector('#wg-join-id') != null,
      { timeout: 30_000 },
    );

    // Sticky activeGame must be cleared — reload should stay on home, not resume the abandoned room.
    await accidentalPage.reload();
    await waitForHome(accidentalPage);

    const gameId = await createGameAsAdmin(hostPage);
    await joinGameViaTile(accidentalPage, gameId);

    await expect(accidentalPage.locator('word-game-widget').locator('.wg-game-id-value')).toHaveText(String(gameId), {
      timeout: 30_000,
    });
    await expect(
      accidentalPage.locator('word-game-widget').locator('[data-action="leave-waiting"]'),
    ).toBeVisible();

    await accidentalContext.close();
    await hostContext.close();
  });
});
