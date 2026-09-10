import { expect, type APIRequestContext, type Page } from '@playwright/test';

const API_BASE = process.env.BFF_URL ?? 'http://localhost:8180';
const REQUIRE_FULL_STACK = process.env.REQUIRE_FULL_STACK === 'true';

function unavailable(message: string): false {
  if (REQUIRE_FULL_STACK) {
    throw new Error(message);
  }
  return false;
}

/** Cached so parallel/sequential specs do not each burn a stack probe. */
let fullStackAvailable: Promise<boolean> | undefined;

/**
 * Lightweight readiness gate for hermetic docker-compose tests.
 * Uses /health only — do not hit /auth/challenge here (auth-ip is 120/min shared
 * across the CI runner; probing would steal budget from PoW auth in the specs).
 */
export async function isFullStackAvailable(request: APIRequestContext): Promise<boolean> {
  fullStackAvailable ??= probeFullStack(request);
  try {
    return await fullStackAvailable;
  } catch (error) {
    fullStackAvailable = undefined;
    throw error;
  }
}

async function probeFullStack(request: APIRequestContext): Promise<boolean> {
  try {
    const healthRes = await request.get(`${API_BASE}/health`);
    if (!healthRes.ok()) {
      return unavailable(`BFF health endpoint returned ${healthRes.status()}`);
    }
    return true;
  } catch (error) {
    if (REQUIRE_FULL_STACK) {
      throw error;
    }
    return false;
  }
}

export async function waitForHome(page: Page): Promise<void> {
  await page.goto('/');
  await page.waitForFunction(
    () =>
      document.querySelector('word-game-widget')?.shadowRoot?.querySelector('[data-action="home-tab-player"]') != null
      && document.querySelector('word-game-widget')?.shadowRoot?.querySelector('#wg-join-id') != null,
    { timeout: 120_000 },
  );
}

export async function createGameAsAdmin(page: Page): Promise<number> {
  await waitForHome(page);
  await page.locator('word-game-widget').locator('[data-action="home-tab-admin"]').click();
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

export async function joinGameViaTile(page: Page, gameId: number): Promise<void> {
  await waitForHome(page);
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

export async function waitForWaitingRoom(page: Page, gameId: number): Promise<void> {
  await page.waitForFunction(
    (id) => {
      const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
      return value?.textContent?.trim() === String(id);
    },
    gameId,
    { timeout: 120_000 },
  );
}

export { API_BASE };
