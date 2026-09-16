import { describe, expect, it } from 'vitest';
import {
  isSessionJoinCode,
  parseSessionJoinCode,
  SESSION_JOIN_CODE_ALPHABET,
  SESSION_JOIN_CODE_LENGTH,
} from '../src/sessionJoinCode.js';

describe('parseSessionJoinCode', () => {
  it('normalizes valid codes to uppercase', () => {
    expect(parseSessionJoinCode('k7m2q')).toBe('K7M2Q');
    expect(parseSessionJoinCode(' AB2CD ')).toBe('AB2CD');
  });

  it('rejects wrong length', () => {
    expect(parseSessionJoinCode('ABCD')).toBeNull();
    expect(parseSessionJoinCode('ABCDEF')).toBeNull();
  });

  it('rejects ambiguous or invalid characters', () => {
    expect(parseSessionJoinCode('O1234')).toBeNull();
    expect(parseSessionJoinCode('I1234')).toBeNull();
    expect(parseSessionJoinCode('L1234')).toBeNull();
    expect(parseSessionJoinCode('A1BCD')).toBeNull();
    expect(parseSessionJoinCode('A0BCD')).toBeNull();
    expect(parseSessionJoinCode('AB-CD')).toBeNull();
  });

  it('exposes alphabet without ambiguous glyphs', () => {
    expect(SESSION_JOIN_CODE_LENGTH).toBe(5);
    expect(SESSION_JOIN_CODE_ALPHABET).not.toMatch(/[01ILO]/);
    expect(isSessionJoinCode('K7M2Q')).toBe(true);
  });
});
