import exec from 'k6/execution';
import { connectHub, getGame, prepareGame } from './lib.js';

const VUS = Number.parseInt(__ENV.VUS || '10', 10);

export const options = {
  scenarios: {
    concurrent_game_clients: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '30s',
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
  const baseUrl = (__ENV.BFF_URL || 'http://localhost:8080').replace(/\/$/, '');
  return prepareGame(baseUrl, VUS);
}

export default function (data) {
  const session = data.sessions[exec.vu.idInTest - 1];
  getGame(data.baseUrl, data.gameId, session.sessionToken);
  connectHub(data.baseUrl, data.gameId, session.sessionToken, 1000);
}
