import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearActiveGame,
  readActiveGame,
  writeActiveGame,
} from '../src/activeGame.js';

const API_BASE = 'http://localhost:8080';

describe('activeGame storage', () => {
  beforeEach(() => {
    const values = new Map<string, string>();
    vi.stubGlobal('sessionStorage', {
      getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => values.set(key, value),
      removeItem: (key: string) => values.delete(key),
      clear: () => values.clear(),
      key: (index: number) => Array.from(values.keys())[index] ?? null,
      get length() {
        return values.size;
      },
    } satisfies Storage);
  });

  afterEach(() => {
    clearActiveGame(API_BASE);
    clearActiveGame('http://localhost:8080/');
    vi.unstubAllGlobals();
  });

  it('round-trips read/write/clear', () => {
    writeActiveGame(API_BASE, { gameId: 42, userId: 'user-1' });
    expect(readActiveGame(API_BASE)).toEqual({ gameId: 42, userId: 'user-1' });

    clearActiveGame(API_BASE);
    expect(readActiveGame(API_BASE)).toBeNull();
  });

  it('normalizes apiBase trailing slash', () => {
    writeActiveGame('http://localhost:8080/', { gameId: 7, userId: 'u1' });
    expect(readActiveGame(API_BASE)).toEqual({ gameId: 7, userId: 'u1' });
  });

  it('returns null for invalid stored payload', () => {
    sessionStorage.setItem('wordgame:activeGame:http://localhost:8080', '{"gameId":"x"}');
    expect(readActiveGame(API_BASE)).toBeNull();
  });
});
