export interface RetryDecision {
  shouldRetry: boolean;
  retryAtUtc?: string;
}

export interface RetryPolicy {
  next(attempt: number, now: Date): RetryDecision;
}

export interface ExponentialBackoffOptions {
  maxAttempts: number;
  initialDelayMs: number;
  maxDelayMs: number;
}

export class ExponentialBackoffRetryPolicy implements RetryPolicy {
  private readonly maxAttempts: number;
  private readonly initialDelayMs: number;
  private readonly maxDelayMs: number;

  public constructor(options: ExponentialBackoffOptions) {
    this.maxAttempts = options.maxAttempts;
    this.initialDelayMs = options.initialDelayMs;
    this.maxDelayMs = options.maxDelayMs;
  }

  public next(attempt: number, now: Date): RetryDecision {
    if (attempt >= this.maxAttempts) {
      return { shouldRetry: false };
    }

    const exponentialDelay = this.initialDelayMs * (2 ** (attempt - 1));
    const delayMs = Math.min(this.maxDelayMs, exponentialDelay);

    return {
      shouldRetry: true,
      retryAtUtc: new Date(now.getTime() + delayMs).toISOString()
    };
  }
}
