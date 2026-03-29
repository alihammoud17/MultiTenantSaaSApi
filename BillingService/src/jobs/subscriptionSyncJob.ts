import { resolve } from 'node:path';
import { BillingCallbackPayload, InternalSubscriptionEvent } from '../shared/types.ts';
import { logger } from '../observability/logger.ts';
import { ExponentialBackoffRetryPolicy } from '../workflows/retryPolicy.ts';
import { FileWorkflowQueue, WorkflowQueue } from '../workflows/workflowQueue.ts';
import { WorkflowProcessor, WorkflowWorker } from '../workflows/workflowWorker.ts';

export interface BillingCallbackPublisher {
  publish(payload: BillingCallbackPayload): Promise<void>;
}

export interface SubscriptionSyncJobResult {
  status: 'queued' | 'duplicate';
  attempts: number;
  payload: BillingCallbackPayload;
}

export interface SubscriptionSyncJobOptions {
  queue?: WorkflowQueue;
  storagePath?: string;
  maxAttempts?: number;
  initialBackoffMs?: number;
  maxBackoffMs?: number;
  workerPollIntervalMs?: number;
}

function toCallbackPayload(event: InternalSubscriptionEvent): BillingCallbackPayload {
  return {
    contractVersion: '2026-03-18',
    eventId: event.eventId,
    eventType: event.eventType,
    provider: event.provider,
    providerEventId: typeof event.payload.providerEventId === 'string' ? event.payload.providerEventId : event.eventId,
    tenantId: event.tenantId,
    subscriptionId: event.subscriptionId,
    targetPlanId: event.targetPlanId,
    occurredAtUtc: event.occurredAt,
    effectiveAtUtc: event.effectiveAt,
    correlationId: event.correlationId
  };
}

export class NoopBillingCallbackPublisher implements BillingCallbackPublisher {
  public async publish(payload: BillingCallbackPayload): Promise<void> {
    logger.info('billing-callback-publisher.noop', {
      eventId: payload.eventId,
      eventType: payload.eventType,
      tenantId: payload.tenantId,
      subscriptionId: payload.subscriptionId,
      correlationId: payload.correlationId
    });
  }
}

class SubscriptionSyncProcessor implements WorkflowProcessor {
  private readonly publisher: BillingCallbackPublisher;

  public constructor(publisher: BillingCallbackPublisher) {
    this.publisher = publisher;
  }

  public async process(event: InternalSubscriptionEvent): Promise<void> {
    const payload = toCallbackPayload(event);
    await this.publisher.publish(payload);
  }
}

export class SubscriptionSyncJob {
  private readonly worker: WorkflowWorker;
  private readonly queue: WorkflowQueue;

  public constructor(publisher: BillingCallbackPublisher = new NoopBillingCallbackPublisher(), options: SubscriptionSyncJobOptions = {}) {
    this.queue = options.queue ?? new FileWorkflowQueue(options.storagePath ?? resolve(process.cwd(), '.billing-workflow-state.json'));

    this.worker = new WorkflowWorker(
      this.queue,
      new SubscriptionSyncProcessor(publisher),
      new ExponentialBackoffRetryPolicy({
        maxAttempts: options.maxAttempts ?? 3,
        initialDelayMs: options.initialBackoffMs ?? 1_000,
        maxDelayMs: options.maxBackoffMs ?? 30_000
      }),
      options.workerPollIntervalMs ?? 2_000
    );
  }

  public startWorker(): void {
    this.worker.start();
  }

  public stopWorker(): void {
    this.worker.stop();
  }

  public async enqueue(event: InternalSubscriptionEvent): Promise<SubscriptionSyncJobResult> {
    const result = await this.queue.enqueue(event);
    const payload = toCallbackPayload(event);

    if (result.status === 'queued') {
      logger.info('subscription-sync-job.queued', {
        eventId: event.eventId,
        eventType: event.eventType,
        tenantId: event.tenantId,
        subscriptionId: event.subscriptionId,
        attempts: result.item.attempts
      });

      void this.worker.processUntilEmpty();
      return {
        status: 'queued',
        attempts: result.item.attempts,
        payload
      };
    }

    logger.info('subscription-sync-job.duplicate', {
      eventId: event.eventId,
      tenantId: event.tenantId,
      subscriptionId: event.subscriptionId,
      status: result.item.status
    });

    return {
      status: 'duplicate',
      attempts: result.item.attempts,
      payload
    };
  }

  public async runWorkerCycle(): Promise<void> {
    await this.worker.processUntilEmpty();
  }

  public async snapshotQueue() {
    return this.queue.snapshot();
  }

  public getQueue(): WorkflowQueue {
    return this.queue;
  }
}

