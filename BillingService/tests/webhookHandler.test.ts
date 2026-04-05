import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { SubscriptionSyncJob } from '../src/jobs/subscriptionSyncJob.ts';
import { BillingProviderAdapter } from '../src/shared/types.ts';
import { SubscriptionWebhookHandler } from '../src/webhooks/handlers/subscriptionWebhookHandler.ts';

function buildNormalizedEvent() {
  return {
    eventId: 'evt_webhook_accepted',
    eventType: 'subscription.plan_changed' as const,
    provider: 'stripe' as const,
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    occurredAt: '2026-03-18T12:00:00.000Z',
    targetPlanId: 'plan-pro',
    correlationId: 'corr_webhook_accepted',
    payload: { providerEventId: 'stripe_evt_accepted' }
  };
}

test('SubscriptionWebhookHandler accepts normalized webhooks and reports duplicate replay safely', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'billing-webhook-test-'));

  try {
    const adapter: BillingProviderAdapter = {
      name: 'placeholder',
      async verifyAndNormalizeWebhook() {
        return {
          accepted: true,
          normalizedEvent: buildNormalizedEvent()
        };
      }
    };

    const syncJob = new SubscriptionSyncJob(undefined, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 2,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const handler = new SubscriptionWebhookHandler(adapter, syncJob);
    const first = await handler.handle({
      rawBody: '{"type":"subscription.updated"}',
      signature: 'sig',
      headers: {}
    });
    for (let i = 0; i < 50; i += 1) {
      const snapshot = await syncJob.snapshotQueue();
      if (snapshot.every((item) => item.status !== 'processing')) {
        break;
      }

      await new Promise((resolve) => setTimeout(resolve, 5));
    }

    const second = await handler.handle({
      rawBody: '{"type":"subscription.updated"}',
      signature: 'sig',
      headers: {}
    });

    assert.equal(first.status, 202);
    assert.equal(first.body.accepted, true);
    assert.equal(first.body.processingStatus, 'queued');
    assert.equal(first.body.eventId, 'evt_webhook_accepted');

    assert.equal(second.status, 202);
    assert.equal(second.body.accepted, true);
    assert.equal(second.body.processingStatus, 'duplicate');
    assert.equal(second.body.eventId, 'evt_webhook_accepted');

    await new Promise((resolve) => setTimeout(resolve, 25));
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});
