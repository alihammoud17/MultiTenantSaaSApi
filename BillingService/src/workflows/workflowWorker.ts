import { logger } from '../observability/logger.ts';
import type { InternalSubscriptionEvent } from '../shared/types.ts';
import type { RetryPolicy } from './retryPolicy.ts';
import type { WorkflowItem, WorkflowQueue } from './workflowQueue.ts';

export interface WorkflowProcessor {
  process(event: InternalSubscriptionEvent): Promise<void>;
}

export class WorkflowWorker {
  private readonly queue: WorkflowQueue;
  private readonly processor: WorkflowProcessor;
  private readonly retryPolicy: RetryPolicy;
  private readonly pollIntervalMs: number;
  private timer: ReturnType<typeof setInterval> | undefined;
  private running = false;

  public constructor(queue: WorkflowQueue, processor: WorkflowProcessor, retryPolicy: RetryPolicy, pollIntervalMs: number) {
    this.queue = queue;
    this.processor = processor;
    this.retryPolicy = retryPolicy;
    this.pollIntervalMs = pollIntervalMs;
  }

  public start(): void {
    if (this.timer) {
      return;
    }

    this.timer = setInterval(() => {
      void this.processUntilEmpty();
    }, this.pollIntervalMs);
  }

  public stop(): void {
    if (!this.timer) {
      return;
    }

    clearInterval(this.timer);
    this.timer = undefined;
  }

  public async processUntilEmpty(): Promise<void> {
    if (this.running) {
      return;
    }

    this.running = true;

    try {
      // eslint-disable-next-line no-constant-condition
      while (true) {
        const claimed = await this.queue.claimNextReady(new Date());
        if (!claimed) {
          return;
        }

        await this.processClaimedItem(claimed);
      }
    } finally {
      this.running = false;
    }
  }

  private async processClaimedItem(item: WorkflowItem): Promise<void> {
    try {
      await this.processor.process(item.event);
      await this.queue.markCompleted(item.eventId, new Date());

      logger.info('workflow-worker.processed', {
        eventId: item.eventId,
        attempts: item.attempts,
        tenantId: item.event.tenantId,
        subscriptionId: item.event.subscriptionId
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unknown processing failure';
      const decision = this.retryPolicy.next(item.attempts, new Date());

      if (!decision.shouldRetry || !decision.retryAtUtc) {
        await this.queue.markDeadLetter(item.eventId, message, new Date());
        logger.error('workflow-worker.dead-lettered', {
          eventId: item.eventId,
          attempts: item.attempts,
          tenantId: item.event.tenantId,
          subscriptionId: item.event.subscriptionId,
          message
        });
        return;
      }

      await this.queue.markForRetry(item.eventId, decision.retryAtUtc, message, new Date());
      logger.warn('workflow-worker.retry-scheduled', {
        eventId: item.eventId,
        attempts: item.attempts,
        retryAtUtc: decision.retryAtUtc,
        tenantId: item.event.tenantId,
        subscriptionId: item.event.subscriptionId,
        message
      });
    }
  }
}
