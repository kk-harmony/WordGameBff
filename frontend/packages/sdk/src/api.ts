import {
  ApiRequestError,
  RateLimitError,
  UnauthorizedError,
} from './errors.js';
import type {
  ApiError,
  CreateGameRequest,
  Game,
  JoinGameRequest,
  MyWordResponse,
  PowChallenge,
  SecretWord,
  Session,
  StartGameRequest,
  UserInfo,
  VoteRequest,
} from './types.js';

const FETCH_TIMEOUT_MS = 30_000;
const GET_RETRY_BACKOFF_MS = 1_000;

function parseRetryAfter(header: string | null): number {
  if (!header) {
    return 60;
  }
  const seconds = Number.parseInt(header, 10);
  if (!Number.isNaN(seconds)) {
    return seconds;
  }
  const date = Date.parse(header);
  if (!Number.isNaN(date)) {
    return Math.max(0, Math.ceil((date - Date.now()) / 1000));
  }
  return 60;
}

async function parseApiError(response: Response): Promise<ApiError> {
  try {
    const body = (await response.json()) as Partial<ApiError> & { type?: string };
    const error = body.error ?? body.type;
    if (error && body.message) {
      return { error, message: body.message };
    }
  } catch {
    // fall through
  }
  return {
    error: 'HTTP_ERROR',
    message: response.statusText || `Request failed with status ${response.status}`,
  };
}

export class ApiClient {
  private readonly apiBase: string;
  private readonly getToken: () => string | null;
  private readonly signal: AbortSignal | undefined;

  constructor(apiBase: string, getToken: () => string | null, signal?: AbortSignal) {
    this.apiBase = apiBase.replace(/\/$/, '');
    this.getToken = getToken;
    this.signal = signal;
  }

  private buildUrl(path: string): string {
    return `${this.apiBase}${path.startsWith('/') ? path : `/${path}`}`;
  }

  private mergeSignal(local?: AbortSignal): AbortSignal | undefined {
    if (!this.signal && !local) {
      return undefined;
    }
    if (!this.signal) {
      return local;
    }
    if (!local) {
      return this.signal;
    }
    const controller = new AbortController();
    const abort = () => controller.abort();
    this.signal.addEventListener('abort', abort);
    local.addEventListener('abort', abort);
    return controller.signal;
  }

  private async fetchWithTimeout(
    url: string,
    init: RequestInit,
    signal?: AbortSignal,
  ): Promise<Response> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
    const merged = this.mergeSignal(signal ?? controller.signal);
    if (merged?.aborted) {
      clearTimeout(timeout);
      throw new DOMException('Aborted', 'AbortError');
    }
    const onAbort = () => controller.abort();
    merged?.addEventListener('abort', onAbort);
    try {
      return await fetch(url, { ...init, signal: merged ?? controller.signal });
    } finally {
      clearTimeout(timeout);
      merged?.removeEventListener('abort', onAbort);
    }
  }

  private authHeaders(): HeadersInit {
    const headers: Record<string, string> = {
      Accept: 'application/json',
    };
    const token = this.getToken();
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
    return headers;
  }

  private async handleResponse<T>(response: Response): Promise<T> {
    if (response.status === 429) {
      const body = await parseApiError(response);
      throw new RateLimitError(body, parseRetryAfter(response.headers.get('Retry-After')));
    }
    if (response.status === 401) {
      const body = await parseApiError(response);
      throw new UnauthorizedError(body);
    }
    if (!response.ok) {
      const body = await parseApiError(response);
      const retryable = response.status >= 500 || response.status === 409;
      throw new ApiRequestError(response.status, body, retryable);
    }
    if (response.status === 204) {
      return undefined as T;
    }
    return (await response.json()) as T;
  }

  private async request<T>(
    method: string,
    path: string,
    body?: unknown,
    retryOnConflict = false,
  ): Promise<T> {
    const url = this.buildUrl(path);
    const init: RequestInit = {
      method,
      headers: {
        ...this.authHeaders(),
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      },
      ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
    };

    try {
      const response = await this.fetchWithTimeout(url, init);
      return await this.handleResponse<T>(response);
    } catch (err) {
      if (retryOnConflict && err instanceof ApiRequestError && err.retryable) {
        await new Promise((r) => setTimeout(r, GET_RETRY_BACKOFF_MS));
        const response = await this.fetchWithTimeout(url, init);
        return await this.handleResponse<T>(response);
      }
      throw err;
    }
  }

  async getChallenge(): Promise<PowChallenge> {
    return this.request<PowChallenge>('GET', '/auth/challenge');
  }

  async verifyChallenge(
    challengeId: string,
    nonce: string,
    userId?: string,
  ): Promise<Session> {
    return this.request<Session>('POST', '/auth/verify', {
      challengeId,
      nonce,
      ...(userId ? { userId } : {}),
    });
  }

  async logout(): Promise<void> {
    await this.request<void>('POST', '/auth/logout');
  }

  async getAuthMe(): Promise<UserInfo> {
    return this.request<UserInfo>('GET', '/api/me', undefined, true);
  }
  async createGame(request: CreateGameRequest): Promise<Game> {
    return this.request<Game>('POST', '/api/games', request);
  }

  async getGame(id: number): Promise<Game> {
    return this.request<Game>('GET', `/api/games/${id}`, undefined, true);
  }
  async joinGame(id: number, request?: JoinGameRequest): Promise<Game> {
    return this.request<Game>('POST', `/api/games/${id}/members`, request ?? {});
  }

  async removeGameMember(gameId: number, memberUserId: string): Promise<Game | void> {
    return this.request<Game | void>(
      'DELETE',
      `/api/games/${gameId}/members/${encodeURIComponent(memberUserId)}`,
    );
  }

  async startGame(id: number, request: StartGameRequest): Promise<Game> {
    return this.request<Game>('POST', `/api/games/${id}/rounds`, request);
  }

  async completeTurn(id: number): Promise<Game> {
    return this.request<Game>('POST', `/api/games/${id}/turns`, undefined, true);
  }

  async getMyWord(id: number): Promise<MyWordResponse> {
    return this.request<MyWordResponse>('GET', `/api/games/${id}/assigned-word`, undefined, true);
  }

  async getWordPair(id: number): Promise<SecretWord> {
    return this.request<SecretWord>('GET', `/api/games/${id}/word-pair`, undefined, true);
  }

  async vote(id: number, request: VoteRequest): Promise<Game> {
    return this.request<Game>('POST', `/api/games/${id}/votes`, request, true);
  }

  async getRandomSecretWord(gameId: number): Promise<SecretWord> {
    return this.request<SecretWord>(
      'GET',
      `/api/games/${gameId}/secret-words/random`,
      undefined,
      true,
    );
  }

  async createSecretWord(request: SecretWord): Promise<SecretWord> {
    return this.request<SecretWord>('POST', '/api/secret-words', request);
  }

  async getSecretWord(id: number, gameId: number): Promise<SecretWord> {
    return this.request<SecretWord>(
      'GET',
      `/api/games/${gameId}/secret-words/${id}`,
      undefined,
      true,
    );
  }
}
