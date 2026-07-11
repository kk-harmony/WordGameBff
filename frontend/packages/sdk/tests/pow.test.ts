import { describe, expect, it } from 'vitest';
import { countLeadingZeroBits, findNonce, sha256, verifyProof } from '../src/pow.js';

describe('countLeadingZeroBits', () => {
  it('counts zero bytes as 8 bits each', () => {
    expect(countLeadingZeroBits(new Uint8Array([0, 0, 0x80]))).toBe(16);
    expect(countLeadingZeroBits(new Uint8Array([0, 0]))).toBe(16);
    expect(countLeadingZeroBits(new Uint8Array([0, 0, 0x08]))).toBe(20);
  });

  it('counts partial leading zeros in first non-zero byte', () => {
    expect(countLeadingZeroBits(new Uint8Array([0x08]))).toBe(4);
    expect(countLeadingZeroBits(new Uint8Array([0x10]))).toBe(3);
    expect(countLeadingZeroBits(new Uint8Array([0x01]))).toBe(7);
  });

  it('returns 0 for hash starting with high bit set', () => {
    expect(countLeadingZeroBits(new Uint8Array([0x80]))).toBe(0);
    expect(countLeadingZeroBits(new Uint8Array([0xff]))).toBe(0);
  });

  it('returns 256 for all-zero hash', () => {
    expect(countLeadingZeroBits(new Uint8Array(32))).toBe(256);
  });
});

describe('verifyProof', () => {
  it('matches C# SolvePow behavior', async () => {
    const prefix = 'abc';
    const nonce = await findNonce(prefix, 1, 0, 1000);
    expect(nonce).not.toBeNull();
    expect(await verifyProof(prefix, nonce!, 1)).toBe(true);

    const hash = await sha256(prefix + nonce!);
    expect(countLeadingZeroBits(hash)).toBeGreaterThanOrEqual(1);
  });

  it('rejects invalid nonce', async () => {
    expect(await verifyProof('abc', 'invalid', 8)).toBe(false);
  });
});
