import { test, expect } from '@playwright/test';
import {
  createGameAsAdmin,
  isFullStackAvailable,
  joinGameViaTile,
  waitForWaitingRoom,
} from './helpers.js';

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
      await joinGameViaTile(joinPage, gameId);
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

  test('returns to session lobby after reload when stored game is finished', async ({ page, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    const sessionCode = await createGameAsAdmin(page);
    await waitForWaitingRoom(page, sessionCode);

    const finishedGameId = 9_001;
    await page.evaluate(
      ({ code, gameId }) => {
        const keys = Object.keys(localStorage).filter((k) => k.startsWith('wordgame:session:'));
        const sessionKey = keys[0];
        if (!sessionKey) {
          return;
        }
        const session = JSON.parse(localStorage.getItem(sessionKey) ?? '{}') as { userId?: string };
        if (!session.userId) {
          return;
        }
        const apiBase = sessionKey.replace('wordgame:session:', '');
        localStorage.setItem(
          `wordgame:activeLobby:${apiBase}`,
          JSON.stringify({ sessionId: code, userId: session.userId, gameId }),
        );
      },
      { code: sessionCode, gameId: finishedGameId },
    );

    await page.route(`**/api/games/${finishedGameId}`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        json: {
          id: finishedGameId,
          name: 'Lobby',
          adminUserId: 'admin',
          status: 'FINISHED',
          outcome: 'IMPOSTOR_IDENTIFIED',
          members: [],
        },
      });
    });

    await page.reload();
    await waitForWaitingRoom(page, sessionCode);
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
