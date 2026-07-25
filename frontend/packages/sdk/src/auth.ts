import { ApiClient } from './api.js';
import { UnauthorizedError } from './errors.js';
import { createInlinePowWorker } from './pow-worker-inline.js';
import type { PowWorkerResponse } from './pow-worker.js';
import { readIdentity, writeIdentity } from './identity.js';
import {
  clearSession,
  readSession,
  toPublicSession,
  writeSession,
} from './session.js';
import type { AuthProgress, Session, SessionPublic } from './types.js';

export interface AuthCallbacks {
  onProgress?: (progress: AuthProgress) => void;
  onSession?: (session: SessionPublic) => void;
  onError?: (error: Error) => void;
}

const REAUTH_BUFFER_MS = 60_000;

export class AuthManager {
  private readonly apiBase: string;
  private readonly callbacks: AuthCallbacks;
  private session: Session | null;
  private api: ApiClient;
  private abortController: AbortController | null = null;
  private reauthTimer: ReturnType<typeof setTimeout> | null = null;
  private worker: Worker | null = null;
  private disposed = false;

  constructor(apiBase: string, callbacks: AuthCallbacks = {}) {
    this.apiBase = apiBase.replace(/\/$/, '');
    this.callbacks = callbacks;
    this.session = readSession(this.apiBase);
    this.api = this.createApiClient();
    if (this.session) {
      this.callbacks.onSession?.(toPublicSession(this.session));
      this.scheduleReauth();
    }
  }

  getSession(): Session | null {
    return this.session;
  }

  getToken(): string | null {
    return this.session?.sessionToken ?? null;
  }

  getApiClient(): ApiClient {
    return this.api;
  }

  private createApiClient(): ApiClient {
    return new ApiClient(
      this.apiBase,
      () => this.getToken(),
      this.abortController?.signal,
    );
  }

  private scheduleReauth(): void {
    if (this.reauthTimer) {
      clearTimeout(this.reauthTimer);
      this.reauthTimer = null;
    }
    if (!this.session) {
      return;
    }
    const expiresAt = new Date(this.session.expiresAt).getTime();
    const delay = expiresAt - Date.now() - REAUTH_BUFFER_MS;
    if (delay <= 0) {
      void this.authenticate();
      return;
    }
    this.reauthTimer = setTimeout(() => {
      void this.authenticate();
    }, delay);
  }

  private setSession(session: Session): void {
    this.session = session;
    writeSession(this.apiBase, session);
    writeIdentity(this.apiBase, { userId: session.userId });
    this.callbacks.onSession?.(toPublicSession(session));
    this.scheduleReauth();
  }

  private terminateWorker(): void {
    if (this.worker) {
      this.worker.terminate();
      this.worker = null;
    }
  }

  private createPowWorker(): Worker {
    return createInlinePowWorker();
  }

  private solvePow(prefix: string, difficulty: number): Promise<string> {
    return new Promise((resolve, reject) => {
      this.terminateWorker();
      const worker = this.createPowWorker();
      this.worker = worker;

      worker.onmessage = (event: MessageEvent<PowWorkerResponse>) => {
        const msg = event.data;
        if (msg.type === 'progress' && msg.iterations !== undefined) {
          this.callbacks.onProgress?.({ iterations: msg.iterations });
        } else if (msg.type === 'found' && msg.nonce) {
          this.terminateWorker();
          resolve(msg.nonce);
        } else if (msg.type === 'exhausted') {
          this.terminateWorker();
          reject(new Error('Unable to solve proof of work.'));
        } else if (msg.type === 'error') {
          this.terminateWorker();
          reject(new Error(msg.message ?? 'Worker error'));
        }
      };

      worker.onerror = (err) => {
        this.terminateWorker();
        reject(new Error(err.message));
      };

      worker.postMessage({
        type: 'solve',
        prefix,
        difficulty,
        start: 0,
        end: Number.MAX_SAFE_INTEGER,
        progressInterval: 1000,
      });
    });
  }

  async authenticate(): Promise<Session> {
    if (this.disposed) {
      throw new Error('AuthManager disposed');
    }

    this.abortController?.abort();
    this.abortController = new AbortController();
    this.api = this.createApiClient();

    try {
      const challenge = await this.api.getChallenge();
      const nonce = await this.solvePow(challenge.prefix, challenge.difficulty);
      const resumeUserId = readIdentity(this.apiBase)?.userId;
      const session = await this.api.verifyChallenge(
        challenge.challengeId,
        nonce,
        resumeUserId,
      );
      this.setSession(session);
      return session;
    } catch (err) {
      const error = err instanceof Error ? err : new Error(String(err));
      this.callbacks.onError?.(error);
      throw error;
    }
  }

  async ensureAuthenticated(): Promise<Session> {
    if (this.session && new Date(this.session.expiresAt).getTime() > Date.now()) {
      this.callbacks.onSession?.(toPublicSession(this.session));
      return this.session;
    }
    return this.authenticate();
  }

  async handleUnauthorized(): Promise<Session> {
    clearSession(this.apiBase);
    this.session = null;
    return this.authenticate();
  }

  async logout(): Promise<void> {
    try {
      if (this.session) {
        await this.api.logout();
      }
    } catch {
      // best effort
    } finally {
      clearSession(this.apiBase);
      this.session = null;
      if (this.reauthTimer) {
        clearTimeout(this.reauthTimer);
        this.reauthTimer = null;
      }
    }
  }

  wrapApiCall<T>(fn: (api: ApiClient) => Promise<T>): Promise<T> {
    const run = async (): Promise<T> => {
      await this.ensureAuthenticated();
      try {
        return await fn(this.api);
      } catch (err) {
        if (err instanceof UnauthorizedError) {
          await this.handleUnauthorized();
          return fn(this.api);
        }
        throw err;
      }
    };
    return run();
  }

  dispose(): void {
    this.disposed = true;
    this.abortController?.abort();
    this.terminateWorker();
    if (this.reauthTimer) {
      clearTimeout(this.reauthTimer);
      this.reauthTimer = null;
    }
  }
}
