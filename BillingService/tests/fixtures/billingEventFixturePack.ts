import type { InternalSubscriptionEvent } from '../../src/shared/types.ts';

export interface BillingEventFixtureStep {
  name: string;
  rawBody: string;
  validSignature: boolean;
  normalizedEvent?: InternalSubscriptionEvent;
}

export interface BillingEventFixtureScenario {
  name: string;
  description: string;
  steps: BillingEventFixtureStep[];
  expected: {
    processingStatuses: Array<'queued' | 'duplicate' | 'rejected'>;
    publishedEventIds: string[];
    persistedEventCount: number;
    occurredAtByEventId?: Record<string, string>;
  };
}

function makeEvent(overrides: Partial<InternalSubscriptionEvent>): InternalSubscriptionEvent {
  return {
    eventId: 'evt_default',
    eventType: 'subscription.renewed',
    provider: 'stripe',
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    occurredAt: '2026-04-10T00:00:00.000Z',
    correlationId: 'corr_default',
    payload: {},
    ...overrides
  };
}

export const billingEventFixturePack: BillingEventFixtureScenario[] = [
  {
    name: 'duplicate-delivery-replay-safety',
    description: 'Second delivery with the same event id is deduplicated while the next event continues through workflow processing.',
    steps: [
      {
        name: 'initial-renewal-delivery',
        rawBody: JSON.stringify({ id: 'stripe_evt_dup_1', type: 'customer.subscription.updated' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_dup_1',
          eventType: 'subscription.renewed',
          occurredAt: '2026-04-09T10:00:00.000Z',
          correlationId: 'corr_dup_1',
          payload: { providerEventId: 'stripe_evt_dup_1' }
        })
      },
      {
        name: 'duplicate-renewal-delivery',
        rawBody: JSON.stringify({ id: 'stripe_evt_dup_1_retry', type: 'customer.subscription.updated' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_dup_1',
          eventType: 'subscription.renewed',
          occurredAt: '2026-04-09T10:00:00.000Z',
          correlationId: 'corr_dup_1_retry',
          payload: { providerEventId: 'stripe_evt_dup_1_retry' }
        })
      },
      {
        name: 'follow-up-plan-change',
        rawBody: JSON.stringify({ id: 'stripe_evt_dup_2', type: 'customer.subscription.updated' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_dup_2',
          eventType: 'subscription.plan_changed',
          occurredAt: '2026-04-09T10:30:00.000Z',
          targetPlanId: 'plan-pro',
          correlationId: 'corr_dup_2',
          payload: { providerEventId: 'stripe_evt_dup_2' }
        })
      }
    ],
    expected: {
      processingStatuses: ['queued', 'duplicate', 'queued'],
      publishedEventIds: ['evt_dup_1', 'evt_dup_2'],
      persistedEventCount: 2
    }
  },
  {
    name: 'out-of-order-deliveries-preserve-arrival-order',
    description: 'Cancellation arrives first, followed by an older renewal and then a plan-change event.',
    steps: [
      {
        name: 'cancellation-arrives-first',
        rawBody: JSON.stringify({ id: 'stripe_evt_order_1', type: 'customer.subscription.deleted' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_order_1',
          eventType: 'subscription.canceled',
          occurredAt: '2026-04-10T08:05:00.000Z',
          correlationId: 'corr_order_1',
          payload: { providerEventId: 'stripe_evt_order_1' }
        })
      },
      {
        name: 'older-renewal-arrives-late',
        rawBody: JSON.stringify({ id: 'stripe_evt_order_2', type: 'invoice.paid' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_order_2',
          eventType: 'subscription.renewed',
          occurredAt: '2026-04-10T08:00:00.000Z',
          correlationId: 'corr_order_2',
          payload: { providerEventId: 'stripe_evt_order_2' }
        })
      },
      {
        name: 'plan-change-delivered-afterward',
        rawBody: JSON.stringify({ id: 'stripe_evt_order_3', type: 'customer.subscription.updated' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_order_3',
          eventType: 'subscription.plan_changed',
          occurredAt: '2026-04-10T08:10:00.000Z',
          targetPlanId: 'plan-enterprise',
          correlationId: 'corr_order_3',
          payload: { providerEventId: 'stripe_evt_order_3' }
        })
      }
    ],
    expected: {
      processingStatuses: ['queued', 'queued', 'queued'],
      publishedEventIds: ['evt_order_1', 'evt_order_2', 'evt_order_3'],
      persistedEventCount: 3,
      occurredAtByEventId: {
        evt_order_1: '2026-04-10T08:05:00.000Z',
        evt_order_2: '2026-04-10T08:00:00.000Z',
        evt_order_3: '2026-04-10T08:10:00.000Z'
      }
    }
  },
  {
    name: 'stale-timestamp-event-remains-replay-safe',
    description: 'A stale payment-failure timestamp remains accepted locally and a newer event still processes normally.',
    steps: [
      {
        name: 'stale-payment-failed',
        rawBody: JSON.stringify({ id: 'stripe_evt_stale_1', type: 'invoice.payment_failed' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_stale_1',
          eventType: 'invoice.payment_failed',
          occurredAt: '2025-01-01T00:00:00.000Z',
          correlationId: 'corr_stale_1',
          payload: { providerEventId: 'stripe_evt_stale_1', staleTimestampFixture: true }
        })
      },
      {
        name: 'newer-grace-period-started',
        rawBody: JSON.stringify({ id: 'stripe_evt_stale_2', type: 'customer.subscription.updated' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_stale_2',
          eventType: 'subscription.grace_period_started',
          occurredAt: '2026-04-10T11:00:00.000Z',
          correlationId: 'corr_stale_2',
          payload: { providerEventId: 'stripe_evt_stale_2' }
        })
      }
    ],
    expected: {
      processingStatuses: ['queued', 'queued'],
      publishedEventIds: ['evt_stale_1', 'evt_stale_2'],
      persistedEventCount: 2,
      occurredAtByEventId: {
        evt_stale_1: '2025-01-01T00:00:00.000Z',
        evt_stale_2: '2026-04-10T11:00:00.000Z'
      }
    }
  },
  {
    name: 'invalid-signature-rejected-before-normalization',
    description: 'Invalid signature is rejected and does not enter the workflow queue; subsequent valid event is processed.',
    steps: [
      {
        name: 'invalid-signature-event',
        rawBody: JSON.stringify({ id: 'stripe_evt_sig_1', type: 'customer.subscription.updated' }),
        validSignature: false
      },
      {
        name: 'valid-follow-up-event',
        rawBody: JSON.stringify({ id: 'stripe_evt_sig_2', type: 'customer.subscription.deleted' }),
        validSignature: true,
        normalizedEvent: makeEvent({
          eventId: 'evt_sig_2',
          eventType: 'subscription.canceled',
          occurredAt: '2026-04-12T09:15:00.000Z',
          correlationId: 'corr_sig_2',
          payload: { providerEventId: 'stripe_evt_sig_2' }
        })
      }
    ],
    expected: {
      processingStatuses: ['rejected', 'queued'],
      publishedEventIds: ['evt_sig_2'],
      persistedEventCount: 1
    }
  }
];
