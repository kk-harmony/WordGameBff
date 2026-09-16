import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import {
  clearActiveLobby,
  readActiveLobby,
  writeActiveLobby,
} from '../src/activeGame.js';

const API_BASE = 'http://localhost:8080';

describe('activeLobby', () => {
  beforeEach(() => {
    const values = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => values.set(key, value),
      removeItem: (key: string) => values.delete(key),
      clear: () => values.clear(),
      key: (index: number) => Array.from(values.keys())[index] ?? null,
      get length() {
        return values.size;
      },
    });
    vi.stubGlobal('sessionStorage', {
      getItem: () => null,
      setItem: () => undefined,
      removeItem: () => undefined,
      clear: () => undefined,
      key: () => null,
      length: 0,
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('stores and reads session lobby with optional gameId', () => {
    writeActiveLobby(API_BASE, { sessionId: 'k7m2q', userId: 'u1', gameId: 42 });
    expect(readActiveLobby(API_BASE)).toEqual({ sessionId: 'K7M2Q', userId: 'u1', gameId: 42 });

    writeActiveLobby(API_BASE, { sessionId: 'K7M2Q', userId: 'u1' });
    expect(readActiveLobby(API_BASE)).toEqual({ sessionId: 'K7M2Q', userId: 'u1' });
  });

  it('clears lobby', () => {
    writeActiveLobby(API_BASE, { sessionId: 'AB2CD', userId: 'u1' });
    clearActiveLobby(API_BASE);
    expect(readActiveLobby(API_BASE)).toBeNull();
  });

  it('rejects invalid join codes including ambiguous L', () => {
    localStorage.setItem(
      `wordgame:activeLobby:${API_BASE}`,
      JSON.stringify({ sessionId: 'L7M2Q', userId: 'u1' }),
    );
    expect(readActiveLobby(API_BASE)).toBeNull();
  });
});
