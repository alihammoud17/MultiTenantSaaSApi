import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { SubscriptionSyncJob } from '../src/jobs/subscriptionSyncJob.ts';
import { InvoiceSyncService } from '../src/services/invoiceSyncService.ts';
import type { TenantBillingProviderGateway } from '../src/providers/tenantBillingGateway.ts';

test('InvoiceSyncService queues failed invoices and ignores non-failed statuses', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'billing-invoice-sync-test-'));
  let syncJob: SubscriptionSyncJob | undefined;

  try {
    const gateway: TenantBillingProviderGateway = {
      async createCheckoutSession() {
        throw new Error('Not needed for this test.');
      },
      async createPortalSession() {
        throw new Error('Not needed for this test.');
      },
      async listInvoicesForSync() {
        return [
          {
            providerInvoiceId: 'in_failed_1',
            providerEventId: 'in_failed_1',
            providerCustomerId: 'cus_123',
            providerSubscriptionId: 'sub_123',
            tenantId: '00000000-0000-0000-0000-000000000001',
            subscriptionId: '00000000-0000-0000-0000-000000000002',
            status: 'open',
            amountDue: 1000,
            amountPaid: 0,
            currency: 'USD',
            occurredAtUtc: '2026-04-01T10:00:00.000Z'
          },
          {
            providerInvoiceId: 'in_paid_1',
            providerEventId: 'in_paid_1',
            providerCustomerId: 'cus_123',
            providerSubscriptionId: 'sub_123',
            tenantId: '00000000-0000-0000-0000-000000000001',
            subscriptionId: '00000000-0000-0000-0000-000000000002',
            status: 'paid',
            amountDue: 1000,
            amountPaid: 1000,
            currency: 'USD',
            occurredAtUtc: '2026-04-01T11:00:00.000Z'
          }
        ];
      }
    };

    syncJob = new SubscriptionSyncJob(undefined, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 2,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const service = new InvoiceSyncService(gateway, syncJob);
    const result = await service.syncInvoices({
      tenantId: '00000000-0000-0000-0000-000000000001',
      subscriptionId: '00000000-0000-0000-0000-000000000002',
      providerCustomerId: 'cus_123',
      providerSubscriptionId: 'sub_123',
      correlationId: 'corr-sync-1',
      limit: 25
    });

    assert.equal(result.fetchedCount, 2);
    assert.equal(result.queuedCount, 1);
    assert.equal(result.ignoredCount, 1);

    const duplicateRun = await service.syncInvoices({
      tenantId: '00000000-0000-0000-0000-000000000001',
      subscriptionId: '00000000-0000-0000-0000-000000000002',
      providerCustomerId: 'cus_123',
      providerSubscriptionId: 'sub_123',
      correlationId: 'corr-sync-1',
      limit: 25
    });

    assert.equal(duplicateRun.duplicateCount, 1);
  } finally {
    syncJob?.stopWorker();
    await rm(dir, { recursive: true, force: true });
  }
});
