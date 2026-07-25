import {
  clearBrowserValue,
  readBrowserJson,
  writeBrowserJson,
} from './browserStorage.js';

const STORAGE_PREFIX = 'wordgame:identity:';

export interface BrowserIdentity {
  userId: string;
}

function isIdentity(value: unknown): value is BrowserIdentity {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as BrowserIdentity).userId === 'string' &&
    Boolean((value as BrowserIdentity).userId)
  );
}

export function readIdentity(apiBase: string): BrowserIdentity | null {
  return readBrowserJson(STORAGE_PREFIX, apiBase, isIdentity);
}

export function writeIdentity(apiBase: string, identity: BrowserIdentity): void {
  if (!identity.userId) {
    return;
  }
  writeBrowserJson(STORAGE_PREFIX, apiBase, identity);
}

export function clearIdentity(apiBase: string): void {
  clearBrowserValue(STORAGE_PREFIX, apiBase);
}
