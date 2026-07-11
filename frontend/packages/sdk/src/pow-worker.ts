/// <reference lib="webworker" />

import { verifyProof } from './pow.js';

export interface PowWorkerRequest {
  type: 'solve';
  prefix: string;
  difficulty: number;
  start: number;
  end: number;
  progressInterval: number;
}

export interface PowWorkerResponse {
  type: 'progress' | 'found' | 'exhausted' | 'error';
  iterations?: number;
  nonce?: string;
  message?: string;
}

self.onmessage = async (event: MessageEvent<PowWorkerRequest>) => {
  const msg = event.data;
  if (msg.type !== 'solve') {
    return;
  }

  try {
    const { prefix, difficulty, start, end, progressInterval } = msg;
    for (let i = start; i < end; i++) {
      const nonce = i.toString();
      if (await verifyProof(prefix, nonce, difficulty)) {
        const found: PowWorkerResponse = { type: 'found', nonce, iterations: i };
        self.postMessage(found);
        return;
      }
      if (i > 0 && i % progressInterval === 0) {
        const progress: PowWorkerResponse = { type: 'progress', iterations: i };
        self.postMessage(progress);
      }
    }
    self.postMessage({ type: 'exhausted' } satisfies PowWorkerResponse);
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Unknown worker error';
    self.postMessage({ type: 'error', message } satisfies PowWorkerResponse);
  }
};
