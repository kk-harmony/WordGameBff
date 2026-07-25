import exec from 'k6/execution';
import { connectHub, getGame, joinGame, mintSession } from './lib.js';

const suppliedTokens = (__ENV.SESSION_TOKENS || '')
  .split(',')
  .map((token) => token.trim())
  .filter(Boolean);
const VUS = suppliedTokens.length || Number.parseInt(__ENV.VUS || '3', 10);

export const options = {
  scenarios: {
    concurrent_game_clients: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '45s',
    },
  },
  thresholds: {
    checks: ['rate>0.95'],
    http_req_failed: ['rate<0.05'],
    'http_req_duration{endpoint:getGame}': ['p(95)<2000'],
    hub_connect_ms: ['p(95)<3000'],
  },
};

export function setup() {
  const baseUrl = (__ENV.BFF_URL || 'https://wordgamebff.fly.dev').replace(/\/$/, '');
  const gameId = Number.parseInt(__ENV.GAME_ID || '', 10);
  if (!Number.isInteger(gameId) || gameId <= 0) {
    throw new Error('GAME_ID must identify an existing waiting game');
  }

  if (suppliedTokens.length > 0) {
    return {
      baseUrl,
      gameId,
      sessions: suppliedTokens.map((sessionToken) => ({ sessionToken })),
    };
  }

  const sessions = [];
  for (let player = 0; player < VUS; player += 1) {
    const session = mintSession(baseUrl);
    joinGame(baseUrl, gameId, session.sessionToken, `Fly load player ${player + 1}`);
    sessions.push(session);
  }
  return { baseUrl, gameId, sessions };
}

export default function (data) {
  const session = data.sessions[exec.vu.idInTest - 1];
  getGame(data.baseUrl, data.gameId, session.sessionToken);
  connectHub(data.baseUrl, data.gameId, session.sessionToken, 2000);
}
