import type { Game } from '@wordgame/sdk';
import {
  isFinishedStatus,
  isLobbyStatus,
  isPlayingStatus,
  isVotingStatus,
} from './sanitize.js';

export function getGameStatusEmoji(status?: string): string {
  if (isLobbyStatus(status)) {
    return '⏳';
  }
  if (isPlayingStatus(status)) {
    return '🎮';
  }
  if (isVotingStatus(status)) {
    return '🗳️';
  }
  if (isFinishedStatus(status)) {
    return '🏁';
  }
  return '❓';
}

export function getOutcomeEmoji(outcome?: string): string {
  if (outcome === 'IMPOSTOR_IDENTIFIED') {
    return '🎯';
  }
  if (outcome === 'IMPOSTOR_SURVIVED') {
    return '😈';
  }
  return '🏁';
}

export function isImpostorWordType(type?: string): boolean {
  return type === 'IMPOSED';
}

export function formatRoundToken(round?: number): string {
  return round != null ? `R${round}` : '';
}

export function formatGameIdToken(id?: number): string {
  return id != null ? `#${id}` : '';
}

export interface GameMetaLine {
  emoji: string;
  parts: string[];
}

export function buildActiveGameMetaLine(game: Game): GameMetaLine {
  const parts: string[] = [];
  const round = formatRoundToken(game.currentRound);
  const id = formatGameIdToken(game.id);
  if (round) {
    parts.push(round);
  }
  if (id) {
    parts.push(id);
  }
  return {
    emoji: getGameStatusEmoji(game.status),
    parts,
  };
}

export function buildFinishedGameMetaLine(
  game: Game,
  outcomeLabel: string,
): { emoji: string; text: string } {
  const parts: string[] = [outcomeLabel];
  const id = formatGameIdToken(game.id);
  if (id) {
    parts.push(id);
  }
  return {
    emoji: getOutcomeEmoji(game.outcome),
    text: parts.join(' · '),
  };
}
