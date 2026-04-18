import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { BillingCallbackPayload, InternalSubscriptionEvent } from '../src/shared/types.ts';
import { BillingCallbackPublisher, SubscriptionSyncJob } from '../src/jobs/subscriptionSyncJob.ts';

function makeEvent(overrides: Partial<InternalSubscriptionEvent> = {}): InternalSubscriptionEvent {
  return {
    eventId: 'evt_contract_123',
    eventType: 'subscription.plan_changed',
    provider: 'stripe',
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    occurredAt: '2026-03-18T12:00:00.000Z',
    effectiveAt: '2026-04-18T12:00:00.000Z',
    targetPlanId: 'plan-pro',
    correlationId: 'corr_contract_123',
    payload: { providerEventId: 'stripe_evt_contract_123' },
    ...overrides
  };
}

async function withTempDir(run: (path: string) => Promise<void>) {
  const dir = await mkdtemp(join(tmpdir(), 'billing-contract-test-'));
  try {
    await run(dir);
  } finally {
    await new Promise((resolve) => setTimeout(resolve, 25));
    await rm(dir, { recursive: true, force: true });
  }
}

test('SubscriptionSyncJob emits callback payload compatible with the internal billing contract', async () => {
  await withTempDir(async (dir) => {
    const published: BillingCallbackPayload[] = [];
    const publisher: BillingCallbackPublisher = {
      async publish(payload) {
        published.push(payload);
      }
    };

    const job = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 1,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const event = makeEvent();
    const result = await job.enqueue(event);
    await job.runWorkerCycle();

    assert.equal(result.status, 'queued');
    assert.equal(published.length, 1);

    const payload = result.payload;
    assert.equal(payload.contractVersion, '2026-03-18');
    assert.equal(payload.eventId, event.eventId);
    assert.equal(payload.eventType, event.eventType);
    assert.equal(payload.provider, event.provider);
    assert.equal(payload.providerEventId, 'stripe_evt_contract_123');
    assert.equal(payload.tenantId, event.tenantId);
    assert.equal(payload.subscriptionId, event.subscriptionId);
    assert.equal(payload.targetPlanId, event.targetPlanId);
    assert.equal(payload.occurredAtUtc, event.occurredAt);
    assert.equal(payload.effectiveAtUtc, event.effectiveAt);
    assert.equal(payload.correlationId, event.correlationId);
  });
});

test('SubscriptionSyncJob falls back providerEventId to eventId for contract safety when provider id is absent', async () => {
  await withTempDir(async (dir) => {
    const publisher: BillingCallbackPublisher = {
      async publish() {
        // no-op for payload assertion from enqueue result
      }
    };

    const job = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 1,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const event = makeEvent({
      eventId: 'evt_contract_fallback',
      payload: {}
    });

    const result = await job.enqueue(event);

    assert.equal(result.payload.providerEventId, 'evt_contract_fallback');
    assert.equal(result.payload.contractVersion, '2026-03-18');
  });
});
