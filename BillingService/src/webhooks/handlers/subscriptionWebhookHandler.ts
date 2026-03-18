import { BillingProviderAdapter } from '../../shared/types.ts';
import { SubscriptionSyncJob } from '../../jobs/subscriptionSyncJob.ts';

export class SubscriptionWebhookHandler {
  private readonly adapter: BillingProviderAdapter;
  private readonly syncJob: SubscriptionSyncJob;

  public constructor(adapter: BillingProviderAdapter, syncJob: SubscriptionSyncJob) {
    this.adapter = adapter;
    this.syncJob = syncJob;
  }

  public async handle(input: {
    rawBody: string;
    signature?: string;
    headers: Record<string, string | string[] | undefined>;
  }): Promise<{ status: number; body: Record<string, unknown> }> {
    const result = await this.adapter.verifyAndNormalizeWebhook(input);

    if (!result.accepted || !result.normalizedEvent) {
      return {
        status: 202,
        body: {
          accepted: false,
          provider: this.adapter.name,
          reason: result.reason ?? 'Webhook ignored.'
        }
      };
    }

    const jobResult = await this.syncJob.enqueue(result.normalizedEvent);

    return {
      status: 202,
      body: {
        accepted: true,
        provider: this.adapter.name,
        eventId: result.normalizedEvent.eventId,
        processingStatus: jobResult.status,
        attempts: jobResult.attempts
      }
    };
  }
}
