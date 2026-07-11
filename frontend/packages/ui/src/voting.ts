import type { Game, GameMember } from '@wordgame/sdk';

export function hasMemberVoted(member: GameMember | undefined): boolean {
  return member?.votedForUserId != null && member.votedForUserId !== '';
}

/** True when an active (non-eliminated) voter is waiting for others. */
export function shouldShowWaitingForVotes(
  isVoting: boolean,
  member: GameMember | undefined,
): boolean {
  return isVoting && !!member && !member.eliminated && hasMemberVoted(member);
}

export function isVoteSelectableMember(
  member: GameMember,
  currentUserId: string | null,
): boolean {
  return member.userId !== currentUserId && !member.eliminated;
}

export function getVoteCandidates(
  members: GameMember[],
  currentUserId: string | null,
): GameMember[] {
  return members.filter((m) => isVoteSelectableMember(m, currentUserId));
}
