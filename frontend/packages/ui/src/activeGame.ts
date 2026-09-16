import {
  clearBrowserValue,
  readBrowserJson,
  writeBrowserJson,
} from '@wordgame/sdk';
import { isSessionJoinCode } from './sessionJoinCode.js';

export interface StoredActiveLobby {
  sessionId: string;
  userId: string;
  gameId?: number;
}

const STORAGE_PREFIX = 'wordgame:activeLobby:';
const LEGACY_PREFIX = 'wordgame:activeGame:';

function isActiveLobby(value: unknown): value is StoredActiveLobby {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const stored = value as StoredActiveLobby;
  return (
    typeof stored.sessionId === 'string' &&
    isSessionJoinCode(stored.sessionId) &&
    typeof stored.userId === 'string' &&
    Boolean(stored.userId) &&
    (stored.gameId === undefined ||
      (typeof stored.gameId === 'number' &&
        Number.isFinite(stored.gameId) &&
        stored.gameId > 0))
  );
}

export function readActiveLobby(apiBase: string): StoredActiveLobby | null {
  const lobby = readBrowserJson(STORAGE_PREFIX, apiBase, isActiveLobby);
  if (lobby) {
    return {
      ...lobby,
      sessionId: lobby.sessionId.toUpperCase(),
    };
  }
  // Legacy sticky games / numeric session ids cannot be resumed — clear them.
  clearBrowserValue(LEGACY_PREFIX, apiBase);
  clearBrowserValue(STORAGE_PREFIX, apiBase);
  return null;
}

export function writeActiveLobby(apiBase: string, active: StoredActiveLobby): void {
  writeBrowserJson(STORAGE_PREFIX, apiBase, {
    ...active,
    sessionId: active.sessionId.toUpperCase(),
  });
}

export function clearActiveLobby(apiBase: string): void {
  clearBrowserValue(STORAGE_PREFIX, apiBase);
  clearBrowserValue(LEGACY_PREFIX, apiBase);
}

/** @deprecated Use readActiveLobby */
export function readActiveGame(apiBase: string): { gameId: number; userId: string } | null {
  const lobby = readActiveLobby(apiBase);
  if (!lobby?.gameId) {
    return null;
  }
  return { gameId: lobby.gameId, userId: lobby.userId };
}

/** @deprecated Use writeActiveLobby */
export function writeActiveGame(
  apiBase: string,
  active: { gameId: number; userId: string },
): void {
  const existing = readActiveLobby(apiBase);
  if (existing) {
    writeActiveLobby(apiBase, {
      sessionId: existing.sessionId,
      userId: active.userId,
      gameId: active.gameId,
    });
  }
}

/** @deprecated Use clearActiveLobby */
export function clearActiveGame(apiBase: string): void {
  clearActiveLobby(apiBase);
}
