import { test, expect } from '@playwright/test';
import { isFullStackAvailable } from './helpers.js';

async function waitForHome(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/');
  await page.waitForFunction(
    () =>
      document.querySelector('word-game-widget')?.shadowRoot?.querySelector('[data-action="start-game"]') != null,
    { timeout: 120_000 },
  );
}

async function createGameAsAdmin(page: import('@playwright/test').Page): Promise<number> {
  await waitForHome(page);
  await page.locator('word-game-widget').locator('[data-action="start-game"]').click();
  await page.waitForFunction(
    () => {
      const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
      return value?.textContent && /^\d+$/.test(value.textContent.trim());
    },
    { timeout: 120_000 },
  );
  const gameIdText = await page.locator('word-game-widget').locator('.wg-game-id-value').textContent();
  expect(gameIdText).toBeTruthy();
  return Number.parseInt(gameIdText!.trim(), 10);
}

async function joinGameViaTile(page: import('@playwright/test').Page, gameId: number): Promise<void> {
  await waitForHome(page);
  await page.locator('word-game-widget').locator('[data-action="show-join"]').click();
  await page.locator('word-game-widget').locator('#wg-join-id').fill(String(gameId));
  await page.locator('word-game-widget').locator('[data-action="join-submit"]').click();
  await page.waitForFunction(
    (id) => {
      const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
      return value?.textContent?.trim() === String(id);
    },
    gameId,
    { timeout: 120_000 },
  );
}

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
});
