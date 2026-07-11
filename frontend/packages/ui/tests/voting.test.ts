import { describe, expect, it } from 'vitest';
import {
  getVoteCandidates,
  hasMemberVoted,
  isVoteSelectableMember,
  shouldShowWaitingForVotes,
} from '../src/voting.js';
import type { GameMember } from '@wordgame/sdk';

const members: GameMember[] = [
  { userId: 'self', role: 'PLAYER' },
  { userId: 'active', role: 'PLAYER' },
  { userId: 'eliminated', role: 'PLAYER', eliminated: true },
];

describe('hasMemberVoted', () => {
  it('is true when votedForUserId is set', () => {
    expect(hasMemberVoted({ userId: 'self', votedForUserId: 'active' })).toBe(true);
  });

  it('is false when votedForUserId is missing', () => {
    expect(hasMemberVoted({ userId: 'self' })).toBe(false);
  });
});

describe('shouldShowWaitingForVotes', () => {
  it('is true when an active voter has submitted', () => {
    expect(
      shouldShowWaitingForVotes(true, { userId: 'self', votedForUserId: 'active' }),
    ).toBe(true);
  });

  it('is false when the player is eliminated', () => {
    expect(
      shouldShowWaitingForVotes(true, {
        userId: 'self',
        eliminated: true,
        votedForUserId: 'active',
      }),
    ).toBe(false);
  });

  it('is false when not voting', () => {
    expect(
      shouldShowWaitingForVotes(false, { userId: 'self', votedForUserId: 'active' }),
    ).toBe(false);
  });
});

describe('isVoteSelectableMember', () => {
  it('excludes the current user', () => {
    expect(isVoteSelectableMember(members[0]!, 'self')).toBe(false);
  });

  it('excludes eliminated members', () => {
    expect(isVoteSelectableMember(members[2]!, 'self')).toBe(false);
  });

  it('includes other active members', () => {
    expect(isVoteSelectableMember(members[1]!, 'self')).toBe(true);
  });
});

describe('getVoteCandidates', () => {
  it('returns only selectable members', () => {
    expect(getVoteCandidates(members, 'self').map((m) => m.userId)).toEqual(['active']);
  });

  it('returns empty when current user is null', () => {
    expect(getVoteCandidates(members, null).map((m) => m.userId)).toEqual(['self', 'active']);
  });
});
