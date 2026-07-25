import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearIdentity, readIdentity, writeIdentity } from '../src/identity.js';

const API_BASE = 'http://localhost:8080';

function stubStorage() {
  const values = new Map<string, string>();
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => {
      values.set(key, value);
    },
    removeItem: (key: string) => {
      values.delete(key);
    },
    clear: () => values.clear(),
    key: (index: number) => Array.from(values.keys())[index] ?? null,
    get length() {
      return values.size;
    },
  } satisfies Storage;
}

describe('identity storage', () => {
  beforeEach(() => {
    vi.stubGlobal('localStorage', stubStorage());
    vi.stubGlobal('sessionStorage', stubStorage());
  });

  afterEach(() => {
    clearIdentity(API_BASE);
    vi.unstubAllGlobals();
  });

  it('round-trips read/write/clear', () => {
    writeIdentity(API_BASE, { userId: '11111111-1111-1111-1111-111111111111' });
    expect(readIdentity(API_BASE)).toEqual({
      userId: '11111111-1111-1111-1111-111111111111',
    });
    clearIdentity(API_BASE);
    expect(readIdentity(API_BASE)).toBeNull();
  });
});
