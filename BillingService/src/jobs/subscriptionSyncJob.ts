import { BillingCallbackPayload, InternalSubscriptionEvent } from '../shared/types.ts';
import { logger } from '../observability/logger.ts';

export interface BillingCallbackPublisher {
  publish(payload: BillingCallbackPayload): Promise<void>;
}

export interface SubscriptionSyncJobResult {
  status: 'processed' | 'duplicate';
  attempts: number;
  payload: BillingCallbackPayload;
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

export class SubscriptionSyncJob {
  private readonly publisher: BillingCallbackPublisher;
  private readonly maxAttempts: number;
  private readonly processedEventIds = new Set<string>();

  public constructor(publisher: BillingCallbackPublisher = new NoopBillingCallbackPublisher(), maxAttempts = 3) {
    this.publisher = publisher;
    this.maxAttempts = maxAttempts;
  }

  public async enqueue(event: InternalSubscriptionEvent): Promise<SubscriptionSyncJobResult> {
    if (this.processedEventIds.has(event.eventId)) {
      const payload = toCallbackPayload(event);
      logger.info('subscription-sync-job.duplicate', {
        eventId: event.eventId,
        tenantId: event.tenantId,
        subscriptionId: event.subscriptionId
      });

      return {
        status: 'duplicate',
        attempts: 0,
        payload
      };
    }

    const payload = toCallbackPayload(event);
    let lastError: unknown;

    for (let attempt = 1; attempt <= this.maxAttempts; attempt += 1) {
      try {
        logger.info('subscription-sync-job.attempt', {
          eventId: event.eventId,
          eventType: event.eventType,
          attempt,
          tenantId: event.tenantId,
          correlationId: event.correlationId
        });

        await this.publisher.publish(payload);
        this.processedEventIds.add(event.eventId);

        logger.info('subscription-sync-job.processed', {
          eventId: event.eventId,
          eventType: event.eventType,
          attempt,
          tenantId: event.tenantId
        });

        return {
          status: 'processed',
          attempts: attempt,
          payload
        };
      } catch (error) {
        lastError = error;
        logger.warn('subscription-sync-job.retry', {
          eventId: event.eventId,
          eventType: event.eventType,
          attempt,
          tenantId: event.tenantId,
          message: error instanceof Error ? error.message : 'Unknown error'
        });
      }
    }

    throw lastError instanceof Error ? lastError : new Error('Subscription sync job failed.');
  }
}
