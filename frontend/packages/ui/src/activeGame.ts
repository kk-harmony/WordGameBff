export interface StoredActiveGame {
  gameId: number;
  userId: string;
}

const STORAGE_PREFIX = 'wordgame:activeGame:';

function storageKey(apiBase: string): string {
  return `${STORAGE_PREFIX}${apiBase.replace(/\/$/, '')}`;
}

export function readActiveGame(apiBase: string): StoredActiveGame | null {
  try {
    const raw = sessionStorage.getItem(storageKey(apiBase));
    if (!raw) {
      return null;
    }
    const stored = JSON.parse(raw) as StoredActiveGame;
    if (
      typeof stored.gameId !== 'number' ||
      !Number.isFinite(stored.gameId) ||
      stored.gameId <= 0 ||
      typeof stored.userId !== 'string' ||
      !stored.userId
    ) {
      return null;
    }
    return stored;
  } catch {
    return null;
  }
}

export function writeActiveGame(apiBase: string, active: StoredActiveGame): void {
  try {
    sessionStorage.setItem(storageKey(apiBase), JSON.stringify(active));
  } catch {
    // sessionStorage unavailable
  }
}

export function clearActiveGame(apiBase: string): void {
  try {
    sessionStorage.removeItem(storageKey(apiBase));
  } catch {
    // sessionStorage unavailable
  }
}
