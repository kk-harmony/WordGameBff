import type { ApiError } from './types.js';

export class ApiRequestError extends Error {
  readonly status: number;
  readonly error: string;
  readonly retryable: boolean;

  constructor(status: number, body: ApiError, retryable = false) {
    super(body.message);
    this.name = 'ApiRequestError';
    this.status = status;
    this.error = body.error;
    this.retryable = retryable;
  }
}

export class RateLimitError extends ApiRequestError {
  readonly retryAfterSeconds: number;

  constructor(body: ApiError, retryAfterSeconds: number) {
    super(429, body, true);
    this.name = 'RateLimitError';
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

export class UnauthorizedError extends ApiRequestError {
  constructor(body: ApiError) {
    super(401, body, true);
    this.name = 'UnauthorizedError';
  }
}

export function isRateLimitError(err: unknown): err is RateLimitError {
  return err instanceof RateLimitError;
}

export function isApiRequestError(err: unknown): err is ApiRequestError {
  return err instanceof ApiRequestError;
}
