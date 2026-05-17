import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { SubscriptionSyncJob, type BillingCallbackPublisher } from '../src/jobs/subscriptionSyncJob.ts';
import type { BillingProviderAdapter, InternalSubscriptionEvent, ProviderWebhookResult } from '../src/shared/types.ts';
import { SubscriptionWebhookHandler } from '../src/webhooks/handlers/subscriptionWebhookHandler.ts';
import {
  billingEventFixturePack,
  type BillingEventFixtureScenario,
  type BillingEventFixtureStep
} from './fixtures/billingEventFixturePack.ts';

class FixtureSequenceAdapter implements BillingProviderAdapter {
  public readonly name = 'stripe' as const;
  private index = 0;
  private readonly steps: BillingEventFixtureStep[];

  public constructor(steps: BillingEventFixtureStep[]) {
    this.steps = steps;
  }

  public async verifyAndNormalizeWebhook(input: {
    rawBody: string;
    signature?: string;
    headers: Record<string, string | string[] | undefined>;
  }): Promise<ProviderWebhookResult> {
    const step = this.steps[this.index];
    this.index += 1;

    if (step === undefined) {
      throw new Error(`Unexpected webhook invocation for raw body: ${input.rawBody}`);
    }
    assert.equal(input.rawBody, step.rawBody);

    if (!step.validSignature) {
      return {
        accepted: false,
        reason: 'Invalid provider signature.'
      };
    }

    if (step.normalizedEvent === undefined) {
      throw new Error(`Expected normalized event for step ${step.name}`);
    }

    return {
      accepted: true,
      normalizedEvent: step.normalizedEvent
    };
  }
}

async function withTempDir(run: (path: string) => Promise<void>) {
  const dir = await mkdtemp(join(tmpdir(), 'billing-fixture-pack-'));
  try {
    await run(dir);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
}

async function settleQueue(job: SubscriptionSyncJob): Promise<void> {
  for (let i = 0; i < 40; i += 1) {
    await job.runWorkerCycle();
    const snapshot = await job.snapshotQueue();
    if (snapshot.every((item) => item.status !== 'processing')) {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, 5));
  }

  throw new Error('Workflow queue did not settle in time.');
}

test('Billing event fixture pack covers duplicate, out-of-order, stale, and invalid-signature replay flows', async (t) => {
  for (const scenario of billingEventFixturePack) {
    await t.test(scenario.name, async () => {
      await runFixtureScenario(scenario);
    });
  }
});

async function runFixtureScenario(scenario: BillingEventFixtureScenario): Promise<void> {
  await withTempDir(async (dir) => {
    const publishedEvents: InternalSubscriptionEvent[] = [];
    const publisher: BillingCallbackPublisher = {
      async publish(payload) {
        publishedEvents.push({
          eventId: payload.eventId,
          eventType: payload.eventType,
          provider: payload.provider,
          tenantId: payload.tenantId,
          subscriptionId: payload.subscriptionId,
          occurredAt: payload.occurredAtUtc,
          effectiveAt: payload.effectiveAtUtc,
          targetPlanId: payload.targetPlanId,
          correlationId: payload.correlationId,
          payload: { providerEventId: payload.providerEventId }
        });
      }
    };

    const job = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 2,
      initialBackoffMs: 1,
      maxBackoffMs: 10,
      workerPollIntervalMs: 5
    });

    const adapter = new FixtureSequenceAdapter(scenario.steps);
    const handler = new SubscriptionWebhookHandler(adapter, job);

    const responseStatuses: Array<'queued' | 'duplicate' | 'rejected'> = [];

    for (const step of scenario.steps) {
      const response = await handler.handle({
        rawBody: step.rawBody,
        signature: step.validSignature ? 'sig_valid' : 'sig_invalid',
        headers: {
          'x-fixture-scenario': scenario.name
        }
      });

      if (step.validSignature) {
        assert.equal(response.body.accepted, true);
        assert.equal(response.status, 202);
        responseStatuses.push((response.body.processingStatus as 'queued' | 'duplicate') ?? 'queued');
      } else {
        assert.equal(response.body.accepted, false);
        assert.equal(response.status, 202);
        responseStatuses.push('rejected');
      }

      await settleQueue(job);
    }

    assert.deepEqual(responseStatuses, scenario.expected.processingStatuses);

    const publishedEventIds = publishedEvents.map((event) => event.eventId);
    assert.deepEqual(publishedEventIds, scenario.expected.publishedEventIds);

    for (const [eventId, occurredAt] of Object.entries(scenario.expected.occurredAtByEventId ?? {})) {
      const published = publishedEvents.find((event) => event.eventId === eventId);
      if (!published) {
        throw new Error(`Expected published event ${eventId}`);
      }

      assert.equal(published.occurredAt, occurredAt);
    }

    const snapshot = await job.snapshotQueue();
    assert.equal(snapshot.length, scenario.expected.persistedEventCount);
    assert.equal(snapshot.every((item) => item.status === 'completed'), true);
  });
}
