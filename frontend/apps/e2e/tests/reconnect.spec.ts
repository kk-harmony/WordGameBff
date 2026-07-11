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

async function waitForWaitingRoom(page: import('@playwright/test').Page, gameId: number): Promise<void> {
  await page.waitForFunction(
    (id) => {
      const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
      return value?.textContent?.trim() === String(id);
    },
    gameId,
    { timeout: 120_000 },
  );
}

test.describe('reconnect', () => {
  test('resumes waiting room after page reload', async ({ page, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    const gameId = await createGameAsAdmin(page);
    await waitForWaitingRoom(page, gameId);

    await page.reload();
    await waitForWaitingRoom(page, gameId);

    await expect(page.locator('word-game-widget').locator('[data-action="start"]')).toBeVisible();
  });

  test('resumes active game after page reload', async ({ browser, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    const adminContext = await browser.newContext();
    const player2Context = await browser.newContext();
    const player3Context = await browser.newContext();

    const adminPage = await adminContext.newPage();
    const player2Page = await player2Context.newPage();
    const player3Page = await player3Context.newPage();

    const gameId = await createGameAsAdmin(adminPage);

    for (const joinPage of [player2Page, player3Page]) {
      await waitForHome(joinPage);
      await joinPage.locator('word-game-widget').locator('[data-action="show-join"]').click();
      await joinPage.locator('word-game-widget').locator('#wg-join-id').fill(String(gameId));
      await joinPage.locator('word-game-widget').locator('[data-action="join-submit"]').click();
      await waitForWaitingRoom(joinPage, gameId);
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

    await adminPage.reload();

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

  test('returns to home after reload when stored game is finished', async ({ page, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    const gameId = await createGameAsAdmin(page);
    await waitForWaitingRoom(page, gameId);

    await page.evaluate((id) => {
      const keys = Object.keys(sessionStorage).filter((k) => k.startsWith('wordgame:session:'));
      const sessionKey = keys[0];
      if (!sessionKey) {
        return;
      }
      const session = JSON.parse(sessionStorage.getItem(sessionKey) ?? '{}') as { userId?: string };
      if (!session.userId) {
        return;
      }
      const apiBase = sessionKey.replace('wordgame:session:', '');
      sessionStorage.setItem(
        `wordgame:activeGame:${apiBase}`,
        JSON.stringify({ gameId: id, userId: session.userId }),
      );
    }, gameId);

    await page.route(`**/api/games/${gameId}`, async (route) => {
      const response = await route.fetch();
      const game = (await response.json()) as Record<string, unknown>;
      await route.fulfill({
        response,
        json: { ...game, status: 'FINISHED', outcome: 'IMPOSTOR_IDENTIFIED' },
      });
    });

    await page.reload();
    await waitForHome(page);
  });

  test('resyncs game state after hub disconnect', async ({ page, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    const gameId = await createGameAsAdmin(page);
    await waitForWaitingRoom(page, gameId);

    let blockHub = true;
    await page.route('**/hubs/game**', (route) => {
      if (blockHub) {
        void route.abort();
        return;
      }
      void route.continue();
    });

    await page.waitForTimeout(2_000);
    blockHub = false;

    await page.reload();
    await waitForWaitingRoom(page, gameId);
    await expect(page.locator('word-game-widget').locator('[data-action="start"]')).toBeVisible();
  });
});
