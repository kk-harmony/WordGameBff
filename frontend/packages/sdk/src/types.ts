export interface CreateGameRequest {
  name: string;
  displayName?: string;
}

export interface CreateGameSessionRequest {
  name: string;
  displayName?: string;
}

export interface JoinGameRequest {
  displayName?: string;
}

export interface JoinGameSessionRequest {
  displayName?: string;
}

export interface StartGameRequest {
  secretWordId: number;
}

export interface VoteRequest {
  votedUserId: string;
}

export interface UserInfo {
  userId: string;
}

export interface GameMember {
  id?: number;
  userId: string;
  displayName?: string;
  role?: string;
  turnCompleted?: boolean;
  eliminated?: boolean;
  assignedWord?: string;
  votedForUserId?: string;
  connected?: boolean;
}

export interface GameSessionMember {
  id?: number;
  userId: string;
  displayName?: string;
  role?: string;
  /** Cumulative session points across finished games. */
  score?: number;
}

export interface Game {
  id?: number;
  name: string;
  adminUserId: string;
  status?: string;
  outcome?: string;
  currentRound?: number;
  voteResetCount?: number;
  currentTurnUserId?: string;
  impostorUserId?: string;
  members?: GameMember[];
}

/** Multi-game lobby (distinct from auth Session JWT). */
export interface GameSession {
  /** Public 5-character join code. */
  id?: string;
  name: string;
  adminUserId: string;
  status?: string;
  gamesStartedCount?: number;
  maxGames?: number;
  currentGameId?: number | null;
  currentGameStatus?: string | null;
  members?: GameSessionMember[];
}

export interface SecretWord {
  id?: number;
  authentic: string;
  imposed: string;
}

export interface MyWordResponse {
  word?: string;
  type?: string;
}

export interface PowChallenge {
  challengeId: string;
  prefix: string;
  difficulty: number;
  expiresAt: string;
}

export interface Session {
  sessionToken: string;
  userId: string;
  expiresAt: string;
}

export interface SessionPublic {
  userId: string;
  expiresAt: string;
}

export interface ApiError {
  error: string;
  message: string;
}

export interface VerifyRequest {
  challengeId: string;
  nonce: string;
}

export interface GameRealtimeMessage {
  type: 'gameChanged';
  gameId: number;
  revision: number;
  triggeredBy?: string;
  action?: GameChangeAction;
  /** Viewer-sanitized snapshot; when present, clients apply it without refetching. */
  game?: Game;
}

export type GameChangeAction =
  | 'join'
  | 'leave'
  | 'start'
  | 'turnComplete'
  | 'vote'
  | 'memberRemoved';

export interface AuthProgress {
  iterations: number;
  nonce?: string;
}
