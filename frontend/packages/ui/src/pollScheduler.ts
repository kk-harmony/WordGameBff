/** REST poll interval when SignalR is unavailable. */
export const DISCONNECTED_POLL_MS = 5_000;

export type PollScreen = 'waiting' | 'game';

export interface PollSchedulerCallbacks {
  onTick: () => void;
}

export class GamePollScheduler {
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor(
    private readonly callbacks: PollSchedulerCallbacks,
    private readonly disconnectedMs = DISCONNECTED_POLL_MS,
  ) {}

  startForScreen(screen: PollScreen, realtimeConnected = false): void {
    this.stop();
    if (realtimeConnected || (screen !== 'waiting' && screen !== 'game')) {
      return;
    }
    this.timer = setInterval(() => this.callbacks.onTick(), this.disconnectedMs);
  }

  stop(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
