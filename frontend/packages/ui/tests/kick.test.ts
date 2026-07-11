import { describe, expect, it } from 'vitest';
import type { Game } from '@wordgame/sdk';
import { canKickMember, isMemberOffline } from '../src/kick.js';

const baseGame = (overrides: Partial<Game> = {}): Game => ({
  name: 'Test',
  adminUserId: 'admin',
  status: 'IN_PROGRESS',
  members: [
    { userId: 'admin', role: 'ADMIN', connected: true },
    { userId: 'p2', connected: true },
    { userId: 'p3', connected: true },
    { userId: 'p4', connected: true },
    { userId: 'p5', connected: false },
  ],
  ...overrides,
});

describe('kick helpers', () => {
  it('allows admin to kick disconnected member when at least 3 players', () => {
    const game = baseGame();
    const target = game.members!.find((m) => m.userId === 'p5')!;
    expect(canKickMember(target, game, 'admin')).toBe(true);
  });

  it('allows admin to kick offline player in a 3-player game', () => {
    const game = baseGame({
      members: [
        { userId: 'admin', connected: true },
        { userId: 'p2', connected: false },
        { userId: 'p3', connected: true },
      ],
    });
    const target = game.members!.find((m) => m.userId === 'p2')!;
    expect(canKickMember(target, game, 'admin')).toBe(true);
  });

  it('rejects kick when fewer than 3 members', () => {
    const game = baseGame({
      members: [
        { userId: 'admin', connected: true },
        { userId: 'p2', connected: false },
      ],
    });
    const target = game.members!.find((m) => m.userId === 'p2')!;
    expect(canKickMember(target, game, 'admin')).toBe(false);
  });

  it('rejects kick for connected members', () => {
    const game = baseGame({
      members: [
        ...baseGame().members!,
        { userId: 'p6', connected: true },
      ],
    });
    const target = game.members!.find((m) => m.userId === 'p2')!;
    expect(canKickMember(target, game, 'admin')).toBe(false);
  });

  it('rejects non-admin kick', () => {
    const game = baseGame();
    const target = game.members!.find((m) => m.userId === 'p5')!;
    expect(canKickMember(target, game, 'p2')).toBe(false);
  });

  it('detects offline members', () => {
    expect(isMemberOffline({ userId: 'x', connected: false })).toBe(true);
    expect(isMemberOffline({ userId: 'x', connected: true })).toBe(false);
    expect(isMemberOffline({ userId: 'x' })).toBe(false);
  });
});
