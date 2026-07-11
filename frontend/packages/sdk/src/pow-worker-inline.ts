export const POW_WORKER_SCRIPT = `
self.onmessage = async (event) => {
  const msg = event.data;
  if (msg.type !== 'solve') return;

  function countLeadingZeroBits(hash) {
    let count = 0;
    for (const b of hash) {
      if (b === 0) { count += 8; continue; }
      for (let bit = 7; bit >= 0; bit--) {
        if ((b & (1 << bit)) === 0) count++;
        else return count;
      }
    }
    return count;
  }

  async function verifyProof(prefix, nonce, difficultyBits) {
    const encoded = new TextEncoder().encode(prefix + nonce);
    const digest = await crypto.subtle.digest('SHA-256', encoded);
    return countLeadingZeroBits(new Uint8Array(digest)) >= difficultyBits;
  }

  try {
    const { prefix, difficulty, start, end, progressInterval } = msg;
    for (let i = start; i < end; i++) {
      const nonce = i.toString();
      if (await verifyProof(prefix, nonce, difficulty)) {
        self.postMessage({ type: 'found', nonce, iterations: i });
        return;
      }
      if (i > 0 && i % progressInterval === 0) {
        self.postMessage({ type: 'progress', iterations: i });
      }
    }
    self.postMessage({ type: 'exhausted' });
  } catch (err) {
    self.postMessage({ type: 'error', message: err && err.message ? err.message : 'Worker error' });
  }
};
`;

export function createInlinePowWorker(): Worker {
  const blob = new Blob([POW_WORKER_SCRIPT], { type: 'application/javascript' });
  const url = URL.createObjectURL(blob);
  const worker = new Worker(url);
  worker.addEventListener('error', () => URL.revokeObjectURL(url), { once: true });
  return worker;
}
