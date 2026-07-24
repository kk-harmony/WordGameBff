/** REST poll interval when SignalR is unavailable. */
export const DISCONNECTED_POLL_MS = 5_000;

/** Safety poll while SignalR reports connected. */
export const CONNECTED_SAFETY_POLL_MS = 15_000;

export type PollScreen = 'waiting' | 'game';

export interface PollSchedulerCallbacks {
  onTick: () => void;
}

export class GamePollScheduler {
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor(
    private readonly callbacks: PollSchedulerCallbacks,
    private readonly disconnectedMs = DISCONNECTED_POLL_MS,
    private readonly connectedMs = CONNECTED_SAFETY_POLL_MS,
  ) {}

  startForScreen(screen: PollScreen, realtimeConnected = false): void {
    this.stop();
    const intervalMs = realtimeConnected ? this.connectedMs : this.disconnectedMs;
    this.timer = setInterval(() => this.callbacks.onTick(), intervalMs);
  }

  stop(): void {
    if (!this.timer) {
      return;
    }
    clearInterval(this.timer);
    this.timer = null;
  }
}
