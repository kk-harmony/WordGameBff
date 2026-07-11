import type { APIRequestContext } from '@playwright/test';

const API_BASE = process.env.BFF_URL ?? 'http://localhost:8080';

export async function isFullStackAvailable(request: APIRequestContext): Promise<boolean> {
  try {
    const challengeRes = await request.get(`${API_BASE}/auth/challenge`);
    if (!challengeRes.ok()) {
      return false;
    }
    const challenge = (await challengeRes.json()) as {
      challengeId: string;
      prefix: string;
      difficulty: number;
    };

    let nonce = '0';
    for (let i = 0; i < 1_000_000; i++) {
      const candidate = i.toString();
      const data = new TextEncoder().encode(challenge.prefix + candidate);
      const digest = await crypto.subtle.digest('SHA-256', data);
      const hash = new Uint8Array(digest);
      let bits = 0;
      for (const b of hash) {
        if (b === 0) {
          bits += 8;
          continue;
        }
        for (let bit = 7; bit >= 0; bit--) {
          if ((b & (1 << bit)) === 0) {
            bits++;
          } else {
            break;
          }
        }
        break;
      }
      if (bits >= challenge.difficulty) {
        nonce = candidate;
        break;
      }
    }

    const verifyRes = await request.post(`${API_BASE}/auth/verify`, {
      data: { challengeId: challenge.challengeId, nonce },
    });
    if (!verifyRes.ok()) {
      return false;
    }
    const session = (await verifyRes.json()) as { sessionToken: string };
    const meRes = await request.get(`${API_BASE}/api/me`, {
      headers: { Authorization: `Bearer ${session.sessionToken}` },
    });
    return meRes.ok();
  } catch {
    return false;
  }
}

export { API_BASE };
