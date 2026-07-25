import { describe, expect, it } from 'vitest';
import { canLeaveWaitingRoom } from '../src/waitingRoomActions.js';

describe('canLeaveWaitingRoom', () => {
  it('hides leave for admin', () => {
    expect(canLeaveWaitingRoom(true)).toBe(false);
  });

  it('shows leave for non-admin', () => {
    expect(canLeaveWaitingRoom(false)).toBe(true);
  });
});
