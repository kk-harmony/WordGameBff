/**
 * Runs async work serially, coalescing overlapping calls so only the latest
 * queued invocation runs after the current one finishes.
 */
export class CoalescingAsyncRunner<T = void> {
  private inFlight: Promise<void> | null = null;
  private rerun = false;
  private latestArg: T | undefined;

  run(work: (arg: T | undefined) => Promise<void>, arg?: T): Promise<void> {
    if (arg !== undefined) {
      this.latestArg = arg;
    }

    if (this.inFlight) {
      this.rerun = true;
      return this.inFlight;
    }

    this.inFlight = this.drain(work);
    return this.inFlight;
  }

  private async drain(work: (arg: T | undefined) => Promise<void>): Promise<void> {
    try {
      do {
        this.rerun = false;
        const arg = this.latestArg;
        this.latestArg = undefined;
        await work(arg);
      } while (this.rerun);
    } finally {
      this.inFlight = null;
    }
  }
}
