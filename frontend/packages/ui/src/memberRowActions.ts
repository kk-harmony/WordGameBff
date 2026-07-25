export type MemberRowAction = 'vote' | 'kick';

export interface MemberRowActionInput {
  votingMode: boolean;
  showVoteButton: boolean;
  confirmPending: boolean;
  showKickButton: boolean;
}

/** Ordered action buttons for a member row. Vote and kick may both appear. */
export function resolveMemberRowActions(input: MemberRowActionInput): MemberRowAction[] {
  const actions: MemberRowAction[] = [];
  if (input.showVoteButton && input.votingMode && !input.confirmPending) {
    actions.push('vote');
  }
  if (input.showKickButton) {
    actions.push('kick');
  }
  return actions;
}
