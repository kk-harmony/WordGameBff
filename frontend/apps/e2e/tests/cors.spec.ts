import { test, expect } from '@playwright/test';
import { API_BASE, isFullStackAvailable } from './helpers.js';

test.describe('CORS', () => {
  test('preflight OPTIONS allows Authorization from playground origin', async ({ request }) => {
    const origin = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173';
    const response = await request.fetch(`${API_BASE}/api/me`, {
      method: 'OPTIONS',
      headers: {
        Origin: origin,
        'Access-Control-Request-Method': 'GET',
        'Access-Control-Request-Headers': 'authorization',
      },
    });

    expect(response.status()).toBeLessThan(400);
    const allowOrigin = response.headers()['access-control-allow-origin'];
    expect(allowOrigin).toBeTruthy();
  });

  test('authenticated API call succeeds from browser origin', async ({ page, request }) => {
    test.skip(!(await isFullStackAvailable(request)), 'Requires docker-compose stack with wordgames');

    await page.goto('/');
    await page.waitForFunction(
      () =>
        document.querySelector('word-game-widget')?.shadowRoot?.querySelector('[data-action="start-game"]') != null,
      { timeout: 120_000 },
    );

    const result = await page.evaluate(async (apiBase) => {
      const key = Object.keys(sessionStorage).find((k) => k.startsWith('wordgame:session:'));
      const raw = key ? sessionStorage.getItem(key) : null;
      const sessions = raw ? (JSON.parse(raw) as { sessionToken: string }) : null;
      if (!sessions?.sessionToken) {
        return { ok: false, status: 0 };
      }
      const res = await fetch(`${apiBase}/api/me`, {
        headers: { Authorization: `Bearer ${sessions.sessionToken}` },
      });
      return { ok: res.ok, status: res.status };
    }, API_BASE);

    expect(result.ok).toBe(true);
    expect(result.status).toBe(200);
  });
});
