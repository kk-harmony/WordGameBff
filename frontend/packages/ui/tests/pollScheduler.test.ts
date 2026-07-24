import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { GamePollScheduler } from '../src/pollScheduler.js';

describe('GamePollScheduler', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('fires ticks when realtime is disconnected', () => {
    const onTick = vi.fn();
    const scheduler = new GamePollScheduler({ onTick }, 100, 500);

    scheduler.startForScreen('waiting', false);
    vi.advanceTimersByTime(99);
    expect(onTick).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(onTick).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(100);
    expect(onTick).toHaveBeenCalledTimes(2);
  });

  it('uses slower safety poll when realtime is connected', () => {
    const onTick = vi.fn();
    const scheduler = new GamePollScheduler({ onTick }, 100, 500);

    scheduler.startForScreen('waiting', true);
    vi.advanceTimersByTime(499);
    expect(onTick).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(onTick).toHaveBeenCalledTimes(1);
  });

  it('stop clears active timers', () => {
    const onTick = vi.fn();
    const scheduler = new GamePollScheduler({ onTick }, 100, 500);

    scheduler.startForScreen('game', false);
    scheduler.stop();
    vi.advanceTimersByTime(300);
    expect(onTick).not.toHaveBeenCalled();
  });

  it('startForScreen replaces the previous timer', () => {
    const onTick = vi.fn();
    const scheduler = new GamePollScheduler({ onTick }, 100, 500);

    scheduler.startForScreen('waiting', false);
    scheduler.startForScreen('game', false);
    vi.advanceTimersByTime(100);
    expect(onTick).toHaveBeenCalledTimes(1);
  });
});
