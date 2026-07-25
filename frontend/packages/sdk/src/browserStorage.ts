/** Namespaced browser JSON storage with localStorage primary and sessionStorage migrate-once. */

export function storageKey(prefix: string, apiBase: string): string {
  return `${prefix}${apiBase.replace(/\/$/, '')}`;
}

function safeGet(storage: Storage | undefined, key: string): string | null {
  try {
    return storage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

function safeSet(storage: Storage | undefined, key: string, value: string): void {
  try {
    storage?.setItem(key, value);
  } catch {
    // storage unavailable
  }
}

function safeRemove(storage: Storage | undefined, key: string): void {
  try {
    storage?.removeItem(key);
  } catch {
    // storage unavailable
  }
}

function localStore(): Storage | undefined {
  try {
    return typeof localStorage !== 'undefined' ? localStorage : undefined;
  } catch {
    return undefined;
  }
}

function sessionStore(): Storage | undefined {
  try {
    return typeof sessionStorage !== 'undefined' ? sessionStorage : undefined;
  } catch {
    return undefined;
  }
}

/** Read raw value from localStorage, migrating from sessionStorage when needed. */
export function readBrowserValue(prefix: string, apiBase: string): string | null {
  const key = storageKey(prefix, apiBase);
  const local = localStore();
  const session = sessionStore();
  const fromLocal = safeGet(local, key);
  if (fromLocal != null) {
    return fromLocal;
  }

  const fromSession = safeGet(session, key);
  if (fromSession == null) {
    return null;
  }

  safeSet(local, key, fromSession);
  safeRemove(session, key);
  return fromSession;
}

export function writeBrowserValue(prefix: string, apiBase: string, value: string): void {
  const key = storageKey(prefix, apiBase);
  safeSet(localStore(), key, value);
  safeRemove(sessionStore(), key);
}

export function clearBrowserValue(prefix: string, apiBase: string): void {
  const key = storageKey(prefix, apiBase);
  safeRemove(localStore(), key);
  safeRemove(sessionStore(), key);
}

export function readBrowserJson<T>(
  prefix: string,
  apiBase: string,
  isValid: (value: unknown) => value is T,
): T | null {
  const raw = readBrowserValue(prefix, apiBase);
  if (!raw) {
    return null;
  }
  try {
    const parsed: unknown = JSON.parse(raw);
    return isValid(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

export function writeBrowserJson(prefix: string, apiBase: string, value: unknown): void {
  writeBrowserValue(prefix, apiBase, JSON.stringify(value));
}
