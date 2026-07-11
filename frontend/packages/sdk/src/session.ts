import type { Session } from './types.js';

const STORAGE_PREFIX = 'wordgame:session:';

function storageKey(apiBase: string): string {
  return `${STORAGE_PREFIX}${apiBase.replace(/\/$/, '')}`;
}

export function readSession(apiBase: string): Session | null {
  try {
    const raw = sessionStorage.getItem(storageKey(apiBase));
    if (!raw) {
      return null;
    }
    const session = JSON.parse(raw) as Session;
    if (!session.sessionToken || !session.userId || !session.expiresAt) {
      return null;
    }
    if (new Date(session.expiresAt).getTime() <= Date.now()) {
      sessionStorage.removeItem(storageKey(apiBase));
      return null;
    }
    return session;
  } catch {
    return null;
  }
}

export function writeSession(apiBase: string, session: Session): void {
  sessionStorage.setItem(storageKey(apiBase), JSON.stringify(session));
}

export function clearSession(apiBase: string): void {
  sessionStorage.removeItem(storageKey(apiBase));
}

export function toPublicSession(session: Session): { userId: string; expiresAt: string } {
  return { userId: session.userId, expiresAt: session.expiresAt };
}
