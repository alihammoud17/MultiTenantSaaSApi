import test from 'node:test';
import assert from 'node:assert/strict';
import { BillingCallbackPayload, InternalSubscriptionEvent } from '../src/shared/types.ts';
import { BillingCallbackPublisher, SubscriptionSyncJob } from '../src/jobs/subscriptionSyncJob.ts';

function makeEvent(overrides: Partial<InternalSubscriptionEvent> = {}): InternalSubscriptionEvent {
  return {
    eventId: 'evt_123',
    eventType: 'subscription.downgrade_scheduled',
    provider: 'stripe',
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    occurredAt: '2026-03-18T12:00:00.000Z',
    effectiveAt: '2026-04-18T12:00:00.000Z',
    targetPlanId: 'plan-basic',
    correlationId: 'corr_123',
    payload: { providerEventId: 'stripe_evt_123' },
    ...overrides
  };
}

test('SubscriptionSyncJob retries transient publisher failures and preserves idempotent event payload', async () => {
  const attempts: BillingCallbackPayload[] = [];
  let failuresRemaining = 1;

  const publisher: BillingCallbackPublisher = {
    async publish(payload) {
      attempts.push(payload);
      if (failuresRemaining > 0) {
        failuresRemaining -= 1;
        throw new Error('temporary failure');
      }
    }
  };

  const job = new SubscriptionSyncJob(publisher, 3);
  const result = await job.enqueue(makeEvent());

  assert.equal(result.status, 'processed');
  assert.equal(result.attempts, 2);
  assert.equal(attempts.length, 2);
  assert.equal(JSON.stringify(attempts[0]), JSON.stringify(attempts[1]));
  assert.equal(result.payload.providerEventId, 'stripe_evt_123');
  assert.equal(result.payload.targetPlanId, 'plan-basic');
  assert.equal(result.payload.effectiveAtUtc, '2026-04-18T12:00:00.000Z');
});

test('SubscriptionSyncJob suppresses duplicate event ids after successful processing', async () => {
  const published: BillingCallbackPayload[] = [];

  const publisher: BillingCallbackPublisher = {
    async publish(payload) {
      published.push(payload);
    }
  };

  const job = new SubscriptionSyncJob(publisher, 2);
  const event = makeEvent({ eventId: 'evt_duplicate', eventType: 'invoice.payment_failed', effectiveAt: '2026-03-25T12:00:00.000Z' });

  const first = await job.enqueue(event);
  const second = await job.enqueue(event);

  assert.equal(first.status, 'processed');
  assert.equal(first.attempts, 1);
  assert.equal(second.status, 'duplicate');
  assert.equal(second.attempts, 0);
  assert.equal(published.length, 1);
  assert.equal(published[0].eventType, 'invoice.payment_failed');
  assert.equal(published[0].effectiveAtUtc, '2026-03-25T12:00:00.000Z');
});
