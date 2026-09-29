import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { ApiClient } from '../src/api.js';
import { RateLimitError } from '../src/errors.js';

describe('ApiClient', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('createSession sends POST with bearer token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 'K7M2Q', name: 'Lobby', adminUserId: 'u1' }), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'test-token');
    const session = await client.createSession({ name: 'Lobby' });

    expect(session.id).toBe('K7M2Q');
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:8080/api/sessions',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({
          Authorization: 'Bearer test-token',
        }),
      }),
    );
  });

  it('startSessionGame posts to sessions games path', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 42, name: 'G', adminUserId: 'u1', status: 'IN_PROGRESS' }), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'token');
    const game = await client.startSessionGame('K7M2Q', { secretWordId: 3 });

    expect(game.id).toBe(42);
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:8080/api/sessions/K7M2Q/games',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('getSession joins and removes session members by code', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            id: 'K7M2Q',
            name: 'Lobby',
            adminUserId: 'u1',
            members: [{ userId: 'u1', role: 'ADMIN', score: 5 }],
          }),
          {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          },
        ),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ id: 'K7M2Q', name: 'Lobby', adminUserId: 'u1', members: [] }), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ id: 3, authentic: 'a', imposed: 'b' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'token');
    const session = await client.getSession('K7M2Q');
    expect(session.members?.[0]?.score).toBe(5);
    await client.joinSession('K7M2Q', { displayName: 'Alex' });
    await client.removeSessionMember('K7M2Q', 'u2');
    await client.getSessionRandomSecretWord('K7M2Q');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('http://localhost:8080/api/sessions/K7M2Q');
    expect(fetchMock.mock.calls[1]?.[0]).toBe('http://localhost:8080/api/sessions/K7M2Q/members');
    expect(fetchMock.mock.calls[2]?.[0]).toBe('http://localhost:8080/api/sessions/K7M2Q/members/u2');
    expect(fetchMock.mock.calls[3]?.[0]).toBe(
      'http://localhost:8080/api/sessions/K7M2Q/secret-words/random',
    );
  });

  it('createGame sends POST with bearer token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 1, name: 'Test', adminUserId: 'u1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'test-token');
    const game = await client.createGame({ name: 'Test' });

    expect(game.id).toBe(1);
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:8080/api/games',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({
          Authorization: 'Bearer test-token',
        }),
      }),
    );
  });

  it('getGame fetches game by id', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 42, name: 'G', adminUserId: 'u1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'token');
    const game = await client.getGame(42);

    expect(game.id).toBe(42);
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:8080/api/games/42',
      expect.objectContaining({ method: 'GET' }),
    );
  });

  it('parses error body on non-2xx', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ error: 'NOT_FOUND', message: 'Game not found' }), {
          status: 404,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    );

    const client = new ApiClient('http://localhost:8080', () => 'token');
    await expect(client.getGame(1)).rejects.toMatchObject({
      error: 'NOT_FOUND',
      message: 'Game not found',
      status: 404,
    });
  });

  it('throws RateLimitError with Retry-After seconds', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ error: 'RATE_LIMITED', message: 'Too many requests' }), {
          status: 429,
          headers: {
            'Content-Type': 'application/json',
            'Retry-After': '30',
          },
        }),
      ),
    );

    const client = new ApiClient('http://localhost:8080', () => null);
    await expect(client.getChallenge()).rejects.toBeInstanceOf(RateLimitError);

    try {
      await client.getChallenge();
    } catch (err) {
      expect(err).toBeInstanceOf(RateLimitError);
      expect((err as RateLimitError).retryAfterSeconds).toBe(30);
    }
  });

  it('joinGame sends displayName in JSON body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 1, name: 'G', adminUserId: 'u1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'token');
    await client.joinGame(9, { displayName: 'Alex' });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ displayName: 'Alex' });
    expect(fetchMock).toHaveBeenCalledWith('http://localhost:8080/api/games/9/members', expect.any(Object));
  });

  it('getRandomSecretWord fetches random secret word for game admin', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 7, authentic: 'a', imposed: 'b' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'token');
    const word = await client.getRandomSecretWord(9);

    expect(word.id).toBe(7);
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:8080/api/games/9/secret-words/random',
      expect.objectContaining({ method: 'GET' }),
    );
  });

  it('parses upstream type field as error code', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ type: 'NOT_FOUND', message: 'Game not found' }), {
          status: 404,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    );

    const client = new ApiClient('http://localhost:8080', () => 'token');
    await expect(client.getGame(1)).rejects.toMatchObject({
      error: 'NOT_FOUND',
      message: 'Game not found',
      status: 404,
    });
  });

  it('createGame sends optional displayName', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 1, name: 'Test', adminUserId: 'u1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const client = new ApiClient('http://localhost:8080', () => 'token');
    await client.createGame({ name: 'Test', displayName: 'Host' });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ name: 'Test', displayName: 'Host' });
  });
});
