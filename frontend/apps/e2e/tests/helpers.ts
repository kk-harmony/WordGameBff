import { expect, type APIRequestContext, type Page } from '@playwright/test';

const API_BASE = process.env.BFF_URL ?? 'http://localhost:8180';
const REQUIRE_FULL_STACK = process.env.REQUIRE_FULL_STACK === 'true';
const SESSION_CODE_PATTERN = /^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{5}$/i;

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

export async function createGameAsAdmin(page: Page): Promise<string> {
  // Creates a multi-game session lobby; the returned value is the public join code.
  await waitForHome(page);
  await page.locator('word-game-widget').locator('[data-action="home-tab-admin"]').click();
  await page.locator('word-game-widget').locator('[data-action="start-game"]').click();
  await page.waitForFunction(
    (patternSource) => {
      const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
      const text = value?.textContent?.trim() ?? '';
      return new RegExp(patternSource, 'i').test(text);
    },
    SESSION_CODE_PATTERN.source,
    { timeout: 120_000 },
  );
  const sessionCode = (await page.locator('word-game-widget').locator('.wg-game-id-value').textContent())?.trim();
  expect(sessionCode).toBeTruthy();
  expect(sessionCode!).toMatch(SESSION_CODE_PATTERN);
  return sessionCode!.toUpperCase();
}

export async function joinGameViaTile(page: Page, sessionCode: string): Promise<void> {
  await waitForHome(page);
  await page.locator('word-game-widget').locator('#wg-join-id').fill(sessionCode);
  await page.locator('word-game-widget').locator('[data-action="join-submit"]').click();
  await page.waitForFunction(
    (code) => {
      const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
      return value?.textContent?.trim().toUpperCase() === code.toUpperCase();
    },
    sessionCode,
    { timeout: 120_000 },
  );
}

export async function waitForWaitingRoom(page: Page, sessionCode: string): Promise<void> {
  await page.waitForFunction(
    (code) => {
      const value = document.querySelector('word-game-widget')?.shadowRoot?.querySelector('.wg-game-id-value');
      return value?.textContent?.trim().toUpperCase() === code.toUpperCase();
    },
    sessionCode,
    { timeout: 120_000 },
  );
}

/** Drive three in-game clients through turns + one vote round until the finished scoreboard appears. */
export async function playUntilSessionScoreboard(pages: Page[]): Promise<void> {
  const deadline = Date.now() + 180_000;
  while (Date.now() < deadline) {
    for (const page of pages) {
      const root = page.locator('word-game-widget');
      const complete = root.locator('[data-action="complete-turn"]');
      if (await complete.isVisible().catch(() => false)) {
        await complete.click();
        await page.waitForTimeout(400);
      }
    }

    for (const page of pages) {
      const root = page.locator('word-game-widget');
      const pick = root.locator('[data-action="pick-vote"]').first();
      if (await pick.isVisible().catch(() => false)) {
        await pick.click();
        const confirm = root.locator('[data-action="confirm-vote"]');
        await expect(confirm).toBeVisible({ timeout: 10_000 });
        await confirm.click();
        await page.waitForTimeout(400);
      }
    }

    const scoreboardVisible = await pages[0]
      ?.locator('word-game-widget')
      .locator('[data-testid="session-scoreboard"]')
      .isVisible()
      .catch(() => false);
    if (scoreboardVisible) {
      return;
    }
    await pages[0]?.waitForTimeout(500);
  }
  throw new Error('Timed out waiting for session scoreboard after finish');
}

export { API_BASE };
