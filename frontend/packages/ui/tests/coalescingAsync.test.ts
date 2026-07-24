import { describe, expect, it } from 'vitest';
import { CoalescingAsyncRunner } from '../src/coalescingAsync.js';

describe('CoalescingAsyncRunner', () => {
  it('runs work immediately when idle', async () => {
    const runner = new CoalescingAsyncRunner<string>();
    const seen: Array<string | undefined> = [];

    await runner.run(async (arg) => {
      seen.push(arg);
    }, 'a');

    expect(seen).toEqual(['a']);
  });

  it('coalesces overlapping calls to the latest argument', async () => {
    const runner = new CoalescingAsyncRunner<string>();
    const seen: Array<string | undefined> = [];
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });

    const first = runner.run(async (arg) => {
      seen.push(arg);
      await gate;
    }, 'first');

    const second = runner.run(async (arg) => {
      seen.push(arg);
    }, 'second');
    const third = runner.run(async (arg) => {
      seen.push(arg);
    }, 'third');

    release();
    await Promise.all([first, second, third]);

    expect(seen).toEqual(['first', 'third']);
  });
});
