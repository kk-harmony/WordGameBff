import crypto from 'k6/crypto';
import http from 'k6/http';
import ws from 'k6/ws';
import { check, fail } from 'k6';
import { Trend } from 'k6/metrics';

export const hubConnectMs = new Trend('hub_connect_ms', true);

function parseJson(response, context) {
  if (response.status < 200 || response.status >= 300) {
    fail(`${context} failed: HTTP ${response.status} ${response.body}`);
  }
  return response.json();
}

function leadingZeroBits(hexDigest) {
  let bits = 0;
  for (const digit of hexDigest) {
    const value = Number.parseInt(digit, 16);
    if (value === 0) {
      bits += 4;
      continue;
    }
    if (value < 2) bits += 3;
    else if (value < 4) bits += 2;
    else if (value < 8) bits += 1;
    break;
  }
  return bits;
}

function solvePow(prefix, difficulty) {
  for (let nonce = 0; nonce < 10_000_000; nonce += 1) {
    const candidate = String(nonce);
    const digest = crypto.sha256(`${prefix}${candidate}`, 'hex');
    if (leadingZeroBits(digest) >= difficulty) {
      return candidate;
    }
  }
  fail(`Unable to solve ${difficulty}-bit PoW challenge`);
}

export function mintSession(baseUrl) {
  const challenge = parseJson(
    http.get(`${baseUrl}/auth/challenge`, { tags: { endpoint: 'authChallenge' } }),
    'challenge',
  );
  const nonce = solvePow(challenge.prefix, challenge.difficulty);
  return parseJson(
    http.post(
      `${baseUrl}/auth/verify`,
      JSON.stringify({ challengeId: challenge.challengeId, nonce }),
      {
        headers: { 'Content-Type': 'application/json' },
        tags: { endpoint: 'authVerify' },
      },
    ),
    'challenge verification',
  );
}

function authorizedHeaders(token) {
  return {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  };
}

export function createGame(baseUrl, token) {
  const game = parseJson(
    http.post(
      `${baseUrl}/api/games`,
      JSON.stringify({ name: `Load test ${Date.now()}`, displayName: 'Load admin' }),
      {
        headers: authorizedHeaders(token),
        tags: { endpoint: 'createGame' },
      },
    ),
    'game creation',
  );
  return game.id;
}

export function joinGame(baseUrl, gameId, token, displayName) {
  const response = http.post(
    `${baseUrl}/api/games/${gameId}/members`,
    JSON.stringify({ displayName }),
    {
      headers: authorizedHeaders(token),
      tags: { endpoint: 'joinGame' },
    },
  );
  check(response, {
    'game join succeeds': (result) => result.status >= 200 && result.status < 300,
  });
  if (response.status < 200 || response.status >= 300) {
    fail(`game join failed: HTTP ${response.status} ${response.body}`);
  }
}

export function prepareGame(baseUrl, playerCount) {
  const admin = mintSession(baseUrl);
  const gameId = createGame(baseUrl, admin.sessionToken);
  const sessions = [admin];

  for (let player = 1; player < playerCount; player += 1) {
    const session = mintSession(baseUrl);
    joinGame(baseUrl, gameId, session.sessionToken, `Load player ${player}`);
    sessions.push(session);
  }

  return { baseUrl, gameId, sessions };
}

export function getGame(baseUrl, gameId, token) {
  const response = http.get(`${baseUrl}/api/games/${gameId}`, {
    headers: authorizedHeaders(token),
    tags: { endpoint: 'getGame' },
  });
  check(response, {
    'getGame succeeds': (result) => result.status === 200,
  });
}

function toWebSocketBase(baseUrl) {
  return baseUrl.replace(/^http:/, 'ws:').replace(/^https:/, 'wss:');
}

export function connectHub(baseUrl, gameId, token, holdMs = 1000) {
  const url = `${toWebSocketBase(baseUrl)}/hubs/game?gameId=${gameId}&access_token=${encodeURIComponent(token)}`;
  const startedAt = Date.now();
  let handshakeCompleted = false;

  const response = ws.connect(url, { tags: { endpoint: 'hubConnect' } }, (socket) => {
    socket.on('open', () => {
      socket.send('{"protocol":"json","version":1}\u001e');
    });

    socket.on('message', (message) => {
      if (!handshakeCompleted && String(message).includes('{}')) {
        handshakeCompleted = true;
        hubConnectMs.add(Date.now() - startedAt);
        socket.setTimeout(() => socket.close(), holdMs);
      }
    });

    socket.setTimeout(() => socket.close(), Math.max(holdMs + 5000, 8000));
  });

  check(response, {
    'hub upgrades to WebSocket': (result) => result && result.status === 101,
    'SignalR handshake completes': () => handshakeCompleted,
  });
}
