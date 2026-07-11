import type { Game, GameMember } from '@wordgame/sdk';
import { isFinishedStatus, isLobbyStatus } from './sanitize.js';

/** Minimum members in game before admin can remove an offline player. */
export const KICK_MIN_MEMBERS = 3;

export function canKickMember(
  member: GameMember,
  game: Game,
  currentUserId: string | null,
): boolean {
  if (!currentUserId) {
    return false;
  }
  const members = game.members ?? [];
  if (members.length < KICK_MIN_MEMBERS) {
    return false;
  }
  if (game.adminUserId !== currentUserId) {
    return false;
  }
  if (isLobbyStatus(game.status) || isFinishedStatus(game.status)) {
    return false;
  }
  if (member.userId === currentUserId || member.userId === game.adminUserId) {
    return false;
  }
  return member.connected === false;
}

export function isMemberOffline(member: GameMember): boolean {
  return member.connected === false;
}
