import { describe, expect, it } from 'vitest';
import { gameStateFingerprint, isUnchangedGameState, sanitizeGame } from '../src/sanitize.js';
import type { Game } from '@wordgame/sdk';

const baseGame: Game = {
  id: 1,
  name: 'Test',
  adminUserId: 'admin',
  status: 'WAITING',
  members: [
    { userId: 'u2', displayName: 'Bob', role: 'PLAYER' },
    { userId: 'u1', displayName: 'Alice', role: 'ADMIN' },
  ],
};

describe('gameStateFingerprint', () => {
  it('is stable regardless of member order', () => {
    const reversed: Game = {
      ...baseGame,
      members: [...(baseGame.members ?? [])].reverse(),
    };
    expect(gameStateFingerprint(baseGame)).toBe(gameStateFingerprint(reversed));
  });

  it('changes when status changes', () => {
    const before = gameStateFingerprint(baseGame);
    const after = gameStateFingerprint({ ...baseGame, status: 'IN_PROGRESS' });
    expect(before).not.toBe(after);
  });

  it('changes when member turnCompleted changes', () => {
    const before = gameStateFingerprint(baseGame);
    const after = gameStateFingerprint({
      ...baseGame,
      members: [
        { userId: 'u1', displayName: 'Alice', role: 'ADMIN', turnCompleted: true },
        { userId: 'u2', displayName: 'Bob', role: 'PLAYER' },
      ],
    });
    expect(before).not.toBe(after);
  });

  it('changes when voteResetCount changes', () => {
    const before = gameStateFingerprint({ ...baseGame, status: 'VOTING', voteResetCount: 0 });
    const after = gameStateFingerprint({ ...baseGame, status: 'VOTING', voteResetCount: 1 });
    expect(before).not.toBe(after);
  });

  it('changes when active votes are cleared during voting', () => {
    const before = gameStateFingerprint({
      ...baseGame,
      status: 'VOTING',
      voteResetCount: 0,
      members: [
        { userId: 'u1', role: 'ADMIN', votedForUserId: 'u2' },
        { userId: 'u2', role: 'PLAYER' },
      ],
    });
    const after = gameStateFingerprint({
      ...baseGame,
      status: 'VOTING',
      voteResetCount: 0,
      members: [
        { userId: 'u1', role: 'ADMIN' },
        { userId: 'u2', role: 'PLAYER' },
      ],
    });
    expect(before).not.toBe(after);
  });

  it('includes impostorUserId only when the game is finished', () => {
    const active = sanitizeGame({
      name: 'Test',
      adminUserId: 'admin',
      status: 'IN_PROGRESS',
      impostorUserId: 'u2',
      members: [{ userId: 'u1' }],
    });
    const finished = sanitizeGame({
      name: 'Test',
      adminUserId: 'admin',
      status: 'FINISHED',
      impostorUserId: 'u2',
      members: [{ userId: 'u1' }],
    });

    expect(active.impostorUserId).toBeUndefined();
    expect(finished.impostorUserId).toBe('u2');
  });

  it('ignores fields not in sanitized UI state', () => {
    const withExtra: Game = {
      ...baseGame,
      impostorUserId: 'secret',
      members: [
        {
          userId: 'u1',
          displayName: 'Alice',
          role: 'ADMIN',
        },
      ],
    };
    const minimal: Game = {
      ...baseGame,
      members: [{ userId: 'u1', displayName: 'Alice', role: 'ADMIN' }],
    };
    expect(gameStateFingerprint(withExtra)).toBe(gameStateFingerprint(minimal));
  });
});

describe('isUnchangedGameState', () => {
  const game: Game = {
    id: 1,
    name: 'Test',
    adminUserId: 'admin',
    status: 'WAITING',
    members: [{ userId: 'u1', role: 'ADMIN' }],
  };

  it('returns false when there is no previous game', () => {
    expect(isUnchangedGameState(null, game, gameStateFingerprint(game))).toBe(false);
  });

  it('returns false when fingerprint is null', () => {
    expect(isUnchangedGameState(game, game, null)).toBe(false);
  });

  it('returns true when fingerprint matches', () => {
    const fingerprint = gameStateFingerprint(game);
    expect(isUnchangedGameState(game, { ...game }, fingerprint)).toBe(true);
  });

  it('returns false when game state changed', () => {
    const fingerprint = gameStateFingerprint(game);
    expect(isUnchangedGameState(game, { ...game, status: 'IN_PROGRESS' }, fingerprint)).toBe(false);
  });
});
