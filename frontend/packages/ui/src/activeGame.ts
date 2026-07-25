import {
  clearBrowserValue,
  readBrowserJson,
  writeBrowserJson,
} from '@wordgame/sdk';

export interface StoredActiveGame {
  gameId: number;
  userId: string;
}

const STORAGE_PREFIX = 'wordgame:activeGame:';

function isActiveGame(value: unknown): value is StoredActiveGame {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const stored = value as StoredActiveGame;
  return (
    typeof stored.gameId === 'number' &&
    Number.isFinite(stored.gameId) &&
    stored.gameId > 0 &&
    typeof stored.userId === 'string' &&
    Boolean(stored.userId)
  );
}

export function readActiveGame(apiBase: string): StoredActiveGame | null {
  return readBrowserJson(STORAGE_PREFIX, apiBase, isActiveGame);
}

export function writeActiveGame(apiBase: string, active: StoredActiveGame): void {
  writeBrowserJson(STORAGE_PREFIX, apiBase, active);
}

export function clearActiveGame(apiBase: string): void {
  clearBrowserValue(STORAGE_PREFIX, apiBase);
}
