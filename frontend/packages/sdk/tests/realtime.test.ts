import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { GameRealtimeMessage } from '../src/types.js';

const mockStart = vi.fn(async () => {});
const mockStop = vi.fn(async () => {});
const handlers: {
  gameEvent?: (message: GameRealtimeMessage) => void;
  onclose?: () => void;
  onreconnecting?: () => void;
  onreconnected?: () => void;
} = {};

const withUrlCalls: unknown[] = [];

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 1, Disconnected: 0 },
  LogLevel: { Warning: 2 },
  HttpTransportType: { WebSockets: 1 },
  HubConnectionBuilder: class {
    withUrl(url: string, options?: unknown) {
      withUrlCalls.push({ url, options });
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        state: 0,
        on(method: string, handler: (message: GameRealtimeMessage) => void) {
          if (method === 'gameEvent') {
            handlers.gameEvent = handler;
          }
        },
        onclose(handler: () => void) {
          handlers.onclose = handler;
        },
        onreconnecting(handler: () => void) {
          handlers.onreconnecting = handler;
        },
        onreconnected(handler: () => void) {
          handlers.onreconnected = handler;
        },
        start: mockStart,
        stop: mockStop,
      };
    }
  },
}));

import { RealtimeClient, RECEIVE_METHOD } from '../src/realtime.js';

describe('RealtimeClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    withUrlCalls.length = 0;
    delete handlers.gameEvent;
    delete handlers.onclose;
    delete handlers.onreconnecting;
    delete handlers.onreconnected;
  });

  it('connects and delivers lightweight gameChanged notifications', async () => {
    const messages: GameRealtimeMessage[] = [];

    const client = new RealtimeClient(
      'http://localhost:8080',
      1,
      () => 'token',
      { onNotify: (m) => messages.push(m) },
    );

    await client.connect();
    expect(mockStart).toHaveBeenCalled();
    expect(withUrlCalls[0]).toEqual({
      url: 'http://localhost:8080/hubs/game?gameId=1&access_token=token',
      options: { skipNegotiation: true, transport: 1 },
    });

    handlers.gameEvent?.({
      type: 'gameChanged',
      gameId: 1,
      revision: 2,
      triggeredBy: 'u2',
      action: 'vote',
    });

    expect(messages).toHaveLength(1);
    expect(messages[0]?.type).toBe('gameChanged');
    expect(messages[0]?.action).toBe('vote');
    expect(client.getLastRevision()).toBe(2);
    expect(RECEIVE_METHOD).toBe('gameEvent');
  });

  it('drops stale lower-revision notifications', async () => {
    const messages: GameRealtimeMessage[] = [];

    const client = new RealtimeClient(
      'http://localhost:8080',
      1,
      () => 'token',
      { onNotify: (m) => messages.push(m) },
    );

    await client.connect();

    handlers.gameEvent?.({
      type: 'gameChanged',
      gameId: 1,
      revision: 2,
      triggeredBy: 'u2',
    });
    handlers.gameEvent?.({
      type: 'gameChanged',
      gameId: 1,
      revision: 1,
      triggeredBy: 'u1',
    });

    expect(messages).toHaveLength(1);
    expect(messages[0]?.revision).toBe(2);
    expect(client.getLastRevision()).toBe(2);
  });

  it('calls onReconnecting when the hub is reconnecting', async () => {
    let reconnecting = false;

    const client = new RealtimeClient(
      'http://localhost:8080',
      1,
      () => 'token',
      { onReconnecting: () => { reconnecting = true; } },
    );

    await client.connect();
    handlers.onreconnecting?.();

    expect(reconnecting).toBe(true);
  });

  it('calls onReconnected without embedding game state', async () => {
    let reconnected = false;

    const client = new RealtimeClient(
      'http://localhost:8080',
      1,
      () => 'token',
      { onReconnected: () => { reconnected = true; } },
    );

    await client.connect();
    handlers.onreconnected?.();

    expect(reconnected).toBe(true);
  });
});
