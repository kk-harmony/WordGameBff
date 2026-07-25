import { describe, expect, it } from 'vitest';
import { resolveMemberRowActions } from '../src/memberRowActions.js';

describe('resolveMemberRowActions', () => {
  it('shows both vote and kick for an offline votable member', () => {
    expect(
      resolveMemberRowActions({
        votingMode: true,
        showVoteButton: true,
        confirmPending: false,
        showKickButton: true,
      }),
    ).toEqual(['vote', 'kick']);
  });

  it('hides vote while confirmation is pending but keeps kick', () => {
    expect(
      resolveMemberRowActions({
        votingMode: true,
        showVoteButton: true,
        confirmPending: true,
        showKickButton: true,
      }),
    ).toEqual(['kick']);
  });

  it('shows only kick outside voting', () => {
    expect(
      resolveMemberRowActions({
        votingMode: false,
        showVoteButton: false,
        confirmPending: false,
        showKickButton: true,
      }),
    ).toEqual(['kick']);
  });
});
