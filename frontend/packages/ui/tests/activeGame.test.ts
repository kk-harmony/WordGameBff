import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearActiveGame,
  readActiveGame,
  writeActiveGame,
} from '../src/activeGame.js';

const API_BASE = 'http://localhost:8080';

describe('activeGame storage', () => {
  beforeEach(() => {
    const localValues = new Map<string, string>();
    const sessionValues = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => localValues.get(key) ?? null,
      setItem: (key: string, value: string) => localValues.set(key, value),
      removeItem: (key: string) => localValues.delete(key),
      clear: () => localValues.clear(),
      key: (index: number) => Array.from(localValues.keys())[index] ?? null,
      get length() {
        return localValues.size;
      },
    } satisfies Storage);
    vi.stubGlobal('sessionStorage', {
      getItem: (key: string) => sessionValues.get(key) ?? null,
      setItem: (key: string, value: string) => sessionValues.set(key, value),
      removeItem: (key: string) => sessionValues.delete(key),
      clear: () => sessionValues.clear(),
      key: (index: number) => Array.from(sessionValues.keys())[index] ?? null,
      get length() {
        return sessionValues.size;
      },
    } satisfies Storage);
  });

  afterEach(() => {
    clearActiveGame(API_BASE);
    clearActiveGame('http://localhost:8080/');
    vi.unstubAllGlobals();
  });

  it('round-trips read/write/clear via localStorage', () => {
    writeActiveGame(API_BASE, { gameId: 42, userId: 'user-1' });
    expect(readActiveGame(API_BASE)).toEqual({ gameId: 42, userId: 'user-1' });
    expect(localStorage.getItem('wordgame:activeGame:http://localhost:8080')).toContain('42');

    clearActiveGame(API_BASE);
    expect(readActiveGame(API_BASE)).toBeNull();
  });

  it('normalizes apiBase trailing slash', () => {
    writeActiveGame('http://localhost:8080/', { gameId: 7, userId: 'u1' });
    expect(readActiveGame(API_BASE)).toEqual({ gameId: 7, userId: 'u1' });
  });

  it('migrates legacy sessionStorage values', () => {
    sessionStorage.setItem(
      'wordgame:activeGame:http://localhost:8080',
      JSON.stringify({ gameId: 9, userId: 'legacy' }),
    );
    expect(readActiveGame(API_BASE)).toEqual({ gameId: 9, userId: 'legacy' });
    expect(sessionStorage.getItem('wordgame:activeGame:http://localhost:8080')).toBeNull();
    expect(localStorage.getItem('wordgame:activeGame:http://localhost:8080')).toContain('legacy');
  });

  it('returns null for invalid stored payload', () => {
    localStorage.setItem('wordgame:activeGame:http://localhost:8080', '{"gameId":"x"}');
    expect(readActiveGame(API_BASE)).toBeNull();
  });
});
