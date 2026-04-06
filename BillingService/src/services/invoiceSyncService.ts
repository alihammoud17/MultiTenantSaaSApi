import { logger } from '../observability/logger.ts';
import type { SubscriptionSyncJob } from '../jobs/subscriptionSyncJob.ts';
import type { InternalSubscriptionEvent } from '../shared/types.ts';
import type { TenantBillingProviderGateway, TenantInvoiceSyncRequest } from '../providers/tenantBillingGateway.ts';

export interface InvoiceSyncResult {
  fetchedCount: number;
  queuedCount: number;
  duplicateCount: number;
  ignoredCount: number;
}

function mapProviderInvoiceStatusToEventType(status: string): InternalSubscriptionEvent['eventType'] | undefined {
  const normalized = status.trim().toLowerCase();

  switch (normalized) {
    case 'open':
    case 'uncollectible':
      return 'invoice.payment_failed';
    default:
      return undefined;
  }
}

export class InvoiceSyncService {
  private readonly gateway: TenantBillingProviderGateway;
  private readonly syncJob: SubscriptionSyncJob;

  public constructor(gateway: TenantBillingProviderGateway, syncJob: SubscriptionSyncJob) {
    this.gateway = gateway;
    this.syncJob = syncJob;
  }

  public async syncInvoices(input: TenantInvoiceSyncRequest): Promise<InvoiceSyncResult> {
    const invoices = await this.gateway.listInvoicesForSync(input);

    let queuedCount = 0;
    let duplicateCount = 0;
    let ignoredCount = 0;

    for (const invoice of invoices) {
      const eventType = mapProviderInvoiceStatusToEventType(invoice.status);
      if (!eventType) {
        ignoredCount += 1;
        continue;
      }

      const eventId = `invoice_sync_${invoice.providerInvoiceId}_${eventType}`;
      const result = await this.syncJob.enqueue({
        eventId,
        eventType,
        provider: 'stripe',
        tenantId: invoice.tenantId,
        subscriptionId: invoice.subscriptionId,
        occurredAt: invoice.occurredAtUtc,
        effectiveAt: invoice.periodEndUtc,
        correlationId: input.correlationId,
        payload: {
          source: 'invoice_sync',
          providerEventId: invoice.providerEventId,
          providerInvoiceId: invoice.providerInvoiceId,
          providerCustomerId: invoice.providerCustomerId,
          providerSubscriptionId: invoice.providerSubscriptionId,
          amountDue: invoice.amountDue,
          amountPaid: invoice.amountPaid,
          currency: invoice.currency,
          periodStartUtc: invoice.periodStartUtc,
          periodEndUtc: invoice.periodEndUtc,
          status: invoice.status
        }
      });

      if (result.status === 'queued') {
        queuedCount += 1;
      } else {
        duplicateCount += 1;
      }
    }

    logger.info('invoice-sync.completed', {
      correlationId: input.correlationId,
      tenantId: input.tenantId,
      subscriptionId: input.subscriptionId,
      fetchedCount: invoices.length,
      queuedCount,
      duplicateCount,
      ignoredCount
    });

    return {
      fetchedCount: invoices.length,
      queuedCount,
      duplicateCount,
      ignoredCount
    };
  }
}
