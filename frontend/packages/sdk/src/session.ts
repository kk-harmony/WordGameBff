import {
  clearBrowserValue,
  readBrowserJson,
  writeBrowserJson,
} from './browserStorage.js';
import type { Session } from './types.js';

const STORAGE_PREFIX = 'wordgame:session:';

function isSession(value: unknown): value is Session {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const session = value as Session;
  return Boolean(session.sessionToken && session.userId && session.expiresAt);
}

export function readSession(apiBase: string): Session | null {
  const session = readBrowserJson(STORAGE_PREFIX, apiBase, isSession);
  if (!session) {
    return null;
  }
  if (new Date(session.expiresAt).getTime() <= Date.now()) {
    clearBrowserValue(STORAGE_PREFIX, apiBase);
    return null;
  }
  return session;
}

export function writeSession(apiBase: string, session: Session): void {
  writeBrowserJson(STORAGE_PREFIX, apiBase, session);
}

export function clearSession(apiBase: string): void {
  clearBrowserValue(STORAGE_PREFIX, apiBase);
}

export function toPublicSession(session: Session): { userId: string; expiresAt: string } {
  return { userId: session.userId, expiresAt: session.expiresAt };
}
