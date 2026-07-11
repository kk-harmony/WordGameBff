import type { Game, GameMember } from '@wordgame/sdk';

export function sanitizeGame(game: Game): Game {
  const sanitized: Game = {
    name: game.name,
    adminUserId: game.adminUserId,
  };
  if (game.id !== undefined) sanitized.id = game.id;
  if (game.status !== undefined) sanitized.status = game.status;
  if (game.outcome !== undefined) sanitized.outcome = game.outcome;
  if (game.currentRound !== undefined) sanitized.currentRound = game.currentRound;
  sanitized.voteResetCount = game.voteResetCount ?? 0;
  if (game.currentTurnUserId !== undefined) sanitized.currentTurnUserId = game.currentTurnUserId;
  if (isFinishedStatus(game.status) && game.impostorUserId !== undefined) {
    sanitized.impostorUserId = game.impostorUserId;
  }
  if (game.members !== undefined) sanitized.members = game.members.map(sanitizeMember);
  return sanitized;
}

function sanitizeMember(member: GameMember): GameMember {
  const sanitized: GameMember = { userId: member.userId };
  if (member.id !== undefined) sanitized.id = member.id;
  if (member.displayName !== undefined) sanitized.displayName = member.displayName;
  if (member.role !== undefined) sanitized.role = member.role;
  if (member.turnCompleted !== undefined) sanitized.turnCompleted = member.turnCompleted;
  if (member.eliminated !== undefined) sanitized.eliminated = member.eliminated;
  if (member.connected !== undefined) sanitized.connected = member.connected;
  if (member.votedForUserId !== undefined) sanitized.votedForUserId = member.votedForUserId;
  return sanitized;
}

/** Stable fingerprint of UI-visible game state for deduplicating no-op updates. */
export function gameStateFingerprint(game: Game): string {
  const sanitized = sanitizeGame(game);
  const members = [...(sanitized.members ?? [])]
    .sort((a, b) => a.userId.localeCompare(b.userId))
    .map((m) => ({
      userId: m.userId,
      displayName: m.displayName,
      role: m.role,
      turnCompleted: m.turnCompleted,
      eliminated: m.eliminated,
      connected: m.connected,
      votedForUserId: m.votedForUserId ?? null,
    }));
  return JSON.stringify({
    status: sanitized.status,
    outcome: sanitized.outcome,
    currentRound: sanitized.currentRound,
    voteResetCount: sanitized.voteResetCount ?? 0,
    currentTurnUserId: sanitized.currentTurnUserId,
    impostorUserId: sanitized.impostorUserId ?? null,
    members,
  });
}

export function isUnchangedGameState(
  previousGame: Game | null,
  nextGame: Game,
  lastFingerprint: string | null,
): boolean {
  return (
    previousGame != null &&
    lastFingerprint != null &&
    gameStateFingerprint(nextGame) === lastFingerprint
  );
}

export function isLobbyStatus(status?: string): boolean {
  return !status || status === 'WAITING' || status === 'CREATED';
}

export function isPlayingStatus(status?: string): boolean {
  return status === 'IN_PROGRESS' || status === 'PLAYING' || status === 'ACTIVE';
}

export function isVotingStatus(status?: string): boolean {
  return status === 'VOTING' || status === 'VOTE';
}

export function isFinishedStatus(status?: string): boolean {
  return status === 'FINISHED' || status === 'COMPLETED' || status === 'ENDED';
}

/** Resolve whose turn it is; falls back to member order when the API omits currentTurnUserId. */
export function resolveCurrentTurnUserId(game: Game): string | null {
  if (game.currentTurnUserId) {
    return game.currentTurnUserId;
  }
  if (!isPlayingStatus(game.status)) {
    return null;
  }
  const next = [...(game.members ?? [])]
    .filter((m) => !m.eliminated && !m.turnCompleted)
    .sort((a, b) => (a.id ?? Number.MAX_SAFE_INTEGER) - (b.id ?? Number.MAX_SAFE_INTEGER));
  return next[0]?.userId ?? null;
}
