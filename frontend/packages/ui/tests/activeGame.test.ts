import { afterEach, describe, expect, it } from 'vitest';
import {
  clearActiveGame,
  readActiveGame,
  writeActiveGame,
} from '../src/activeGame.js';

const API_BASE = 'http://localhost:8080';

describe('activeGame storage', () => {
  afterEach(() => {
    clearActiveGame(API_BASE);
    clearActiveGame('http://localhost:8080/');
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
