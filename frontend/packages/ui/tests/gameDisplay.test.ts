import { describe, expect, it } from 'vitest';
import {
  buildActiveGameMetaLine,
  buildFinishedGameMetaLine,
  getGameStatusEmoji,
  getOutcomeEmoji,
  isImpostorWordType,
} from '../src/gameDisplay.js';

describe('gameDisplay', () => {
  it('maps statuses to emoji', () => {
    expect(getGameStatusEmoji('WAITING')).toBe('⏳');
    expect(getGameStatusEmoji('IN_PROGRESS')).toBe('🎮');
    expect(getGameStatusEmoji('VOTING')).toBe('🗳️');
    expect(getGameStatusEmoji('FINISHED')).toBe('🏁');
  });

  it('maps outcomes to emoji', () => {
    expect(getOutcomeEmoji('IMPOSTOR_IDENTIFIED')).toBe('🎯');
    expect(getOutcomeEmoji('IMPOSTOR_SURVIVED')).toBe('😈');
  });

  it('detects impostor word type', () => {
    expect(isImpostorWordType('IMPOSED')).toBe(true);
    expect(isImpostorWordType('AUTHENTIC')).toBe(false);
  });

  it('builds compact active meta parts', () => {
    const meta = buildActiveGameMetaLine({
      name: 'G',
      adminUserId: 'a',
      status: 'VOTING',
      currentRound: 2,
      id: 99,
    });
    expect(meta.emoji).toBe('🗳️');
    expect(meta.parts).toEqual(['R2', '#99']);
  });

  it('builds one-line finished meta', () => {
    const line = buildFinishedGameMetaLine(
      { name: 'G', adminUserId: 'a', status: 'FINISHED', outcome: 'IMPOSTOR_IDENTIFIED', id: 7 },
      'Impostor caught!',
    );
    expect(line.emoji).toBe('🎯');
    expect(line.text).toBe('Impostor caught! · #7');
  });
});
