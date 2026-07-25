import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearBrowserValue,
  readBrowserJson,
  readBrowserValue,
  writeBrowserJson,
  writeBrowserValue,
} from '../src/browserStorage.js';

const API_BASE = 'http://localhost:8080';
const PREFIX = 'wordgame:test:';

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
    _values: values,
  } satisfies Storage & { _values: Map<string, string> };
}

describe('browserStorage', () => {
  let local: ReturnType<typeof stubStorage>;
  let session: ReturnType<typeof stubStorage>;

  beforeEach(() => {
    local = stubStorage();
    session = stubStorage();
    vi.stubGlobal('localStorage', local);
    vi.stubGlobal('sessionStorage', session);
  });

  afterEach(() => {
    clearBrowserValue(PREFIX, API_BASE);
    vi.unstubAllGlobals();
  });

  it('writes and reads from localStorage', () => {
    writeBrowserValue(PREFIX, API_BASE, 'hello');
    expect(readBrowserValue(PREFIX, API_BASE)).toBe('hello');
    expect(local.getItem('wordgame:test:http://localhost:8080')).toBe('hello');
  });

  it('migrates sessionStorage into localStorage once', () => {
    session.setItem('wordgame:test:http://localhost:8080', 'legacy');
    expect(readBrowserValue(PREFIX, API_BASE)).toBe('legacy');
    expect(local.getItem('wordgame:test:http://localhost:8080')).toBe('legacy');
    expect(session.getItem('wordgame:test:http://localhost:8080')).toBeNull();
  });

  it('round-trips JSON payloads', () => {
    writeBrowserJson(PREFIX, API_BASE, { userId: 'u1' });
    expect(
      readBrowserJson(PREFIX, API_BASE, (v): v is { userId: string } =>
        typeof v === 'object' && v !== null && typeof (v as { userId: unknown }).userId === 'string'),
    ).toEqual({ userId: 'u1' });
  });
});
