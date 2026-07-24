import type { APIRequestContext } from '@playwright/test';

const API_BASE = process.env.BFF_URL ?? 'http://localhost:8080';
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
 * Uses /health only — do not hit /auth/challenge here (Auth IP rate limit).
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

export { API_BASE };
