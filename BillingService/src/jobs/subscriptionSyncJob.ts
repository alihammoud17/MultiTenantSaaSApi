import { InternalSubscriptionEvent } from '../shared/types.ts';

export class SubscriptionSyncJob {
  public async enqueue(event: InternalSubscriptionEvent): Promise<void> {
    console.info('subscription-sync-job.enqueued', {
      eventId: event.eventId,
      eventType: event.eventType,
      provider: event.provider,
      tenantId: event.tenantId,
      correlationId: event.correlationId
    });
  }
}
