export function countLeadingZeroBits(hash: Uint8Array): number {
  let count = 0;
  for (const b of hash) {
    if (b === 0) {
      count += 8;
      continue;
    }
    for (let bit = 7; bit >= 0; bit--) {
      if ((b & (1 << bit)) === 0) {
        count++;
      } else {
        return count;
      }
    }
  }
  return count;
}

export async function sha256(data: string): Promise<Uint8Array> {
  const encoded = new TextEncoder().encode(data);
  const digest = await crypto.subtle.digest('SHA-256', encoded);
  return new Uint8Array(digest);
}

export async function verifyProof(
  prefix: string,
  nonce: string,
  difficultyBits: number,
): Promise<boolean> {
  const hash = await sha256(prefix + nonce);
  return countLeadingZeroBits(hash) >= difficultyBits;
}

export async function findNonce(
  prefix: string,
  difficultyBits: number,
  start = 0,
  end = Number.MAX_SAFE_INTEGER,
  onProgress?: (iterations: number) => void,
  progressInterval = 1000,
  signal?: AbortSignal,
): Promise<string | null> {
  for (let i = start; i < end; i++) {
    if (signal?.aborted) {
      return null;
    }
    const nonce = i.toString();
    if (await verifyProof(prefix, nonce, difficultyBits)) {
      return nonce;
    }
    if (onProgress && i > 0 && i % progressInterval === 0) {
      onProgress(i);
    }
  }
  return null;
}
