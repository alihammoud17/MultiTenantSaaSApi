import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { BillingCallbackPayload, InternalSubscriptionEvent } from '../src/shared/types.ts';
import {
  InternalStateSource,
  ProviderStateSource,
  ReconciliationJob
} from '../src/jobs/reconciliationJob.ts';
import { BillingCallbackPublisher, SubscriptionSyncJob } from '../src/jobs/subscriptionSyncJob.ts';
import { WorkflowItemStatus } from '../src/workflows/workflowQueue.ts';

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

async function withTempDir(run: (path: string) => Promise<void>) {
  const dir = await mkdtemp(join(tmpdir(), 'billing-workflow-test-'));
  try {
    await run(dir);
  } finally {
    await new Promise((resolve) => setTimeout(resolve, 25));
    await rm(dir, { recursive: true, force: true });
  }
}

async function waitForStatus(job: SubscriptionSyncJob, eventId: string, status: WorkflowItemStatus): Promise<void> {
  for (let i = 0; i < 50; i += 1) {
    await job.runWorkerCycle();
    const snapshot = await job.snapshotQueue();
    if (snapshot.some((item) => item.eventId === eventId && item.status === status)) {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, 5));
  }

  throw new Error(`Timed out waiting for ${eventId} to reach status ${status}`);
}

test('SubscriptionSyncJob queues work and worker retries transient publisher failures with backoff', async () => {
  await withTempDir(async (dir) => {
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

    const job = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 3,
      initialBackoffMs: 1,
      maxBackoffMs: 2_000
    });

    const result = await job.enqueue(makeEvent());
    assert.equal(result.status, 'queued');

    await waitForStatus(job, 'evt_123', 'completed');

    assert.equal(attempts.length, 2);
    assert.equal(JSON.stringify(attempts[0]), JSON.stringify(attempts[1]));

    const snapshot = await job.snapshotQueue();
    assert.equal(snapshot[0].status, 'completed');
    assert.equal(snapshot[0].attempts, 2);
  });
});

test('SubscriptionSyncJob dead-letters an event once retry budget is exhausted', async () => {
  await withTempDir(async (dir) => {
    const publisher: BillingCallbackPublisher = {
      async publish() {
        throw new Error('persistent callback outage');
      }
    };

    const job = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 2,
      initialBackoffMs: 1,
      maxBackoffMs: 2_000
    });

    await job.enqueue(makeEvent({ eventId: 'evt_dead_letter' }));
    await waitForStatus(job, 'evt_dead_letter', 'dead_letter');

    const snapshot = await job.snapshotQueue();
    assert.equal(snapshot[0].status, 'dead_letter');
    assert.equal(snapshot[0].attempts, 2);
    assert.match(snapshot[0].deadLetterReason ?? '', /persistent callback outage/);
  });
});

test('SubscriptionSyncJob keeps dedup/replay protection across process restarts', async () => {
  await withTempDir(async (dir) => {
    const statePath = join(dir, 'state.json');
    const published: string[] = [];

    const firstPublisher: BillingCallbackPublisher = {
      async publish(payload) {
        published.push(payload.eventId);
      }
    };

    const firstJob = new SubscriptionSyncJob(firstPublisher, {
      storagePath: statePath,
      maxAttempts: 2,
      initialBackoffMs: 1,
      maxBackoffMs: 2_000
    });

    await firstJob.enqueue(makeEvent({ eventId: 'evt_persisted' }));
    await waitForStatus(firstJob, 'evt_persisted', 'completed');
    assert.deepEqual(published, ['evt_persisted']);

    const secondPublisher: BillingCallbackPublisher = {
      async publish(payload) {
        published.push(payload.eventId);
      }
    };

    const secondJob = new SubscriptionSyncJob(secondPublisher, {
      storagePath: statePath,
      maxAttempts: 2,
      initialBackoffMs: 1,
      maxBackoffMs: 2_000
    });

    const duplicate = await secondJob.enqueue(makeEvent({ eventId: 'evt_persisted' }));
    assert.equal(duplicate.status, 'duplicate');
    assert.deepEqual(published, ['evt_persisted']);
  });
});

function buildStateSources(): { providerSource: ProviderStateSource; internalSource: InternalStateSource } {
  const providerSource: ProviderStateSource = {
    async listSubscriptions() {
      return [
        {
          tenantId: '00000000-0000-0000-0000-000000000001',
          subscriptionId: '00000000-0000-0000-0000-000000000002',
          status: 'active',
          planId: 'plan-pro'
        }
      ];
    }
  };

  const internalSource: InternalStateSource = {
    async listSubscriptions() {
      return [
        {
          tenantId: '00000000-0000-0000-0000-000000000001',
          subscriptionId: '00000000-0000-0000-0000-000000000002',
          status: 'canceled',
          planId: 'plan-basic'
        }
      ];
    }
  };

  return { providerSource, internalSource };
}

test('ReconciliationJob detects provider/internal drift and queues correction work', async () => {
  await withTempDir(async (dir) => {
    const published: string[] = [];

    const publisher: BillingCallbackPublisher = {
      async publish(payload) {
        published.push(payload.eventId);
      }
    };

    const syncJob = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 3,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const { providerSource, internalSource } = buildStateSources();
    const job = new ReconciliationJob(providerSource, internalSource, syncJob);

    const result = await job.runOnce(new Date('2026-04-05T10:00:00.000Z'));

    assert.equal(result.comparedCount, 1);
    assert.equal(result.driftCount, 1);
    assert.equal(result.queuedActions, 1);
    assert.equal(result.duplicateActions, 0);
    assert.equal(result.drifts[0].reason, 'status_mismatch');

    await waitForStatus(syncJob, result.drifts[0].reconciliationEventId, 'completed');
    assert.equal(published.length, 1);
  });
});

test('ReconciliationJob is safe to rerun and marks duplicate drift actions explicitly', async () => {
  await withTempDir(async (dir) => {
    const published: string[] = [];

    const publisher: BillingCallbackPublisher = {
      async publish(payload) {
        published.push(payload.eventId);
      }
    };

    const syncJob = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 3,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const { providerSource, internalSource } = buildStateSources();
    const job = new ReconciliationJob(providerSource, internalSource, syncJob);

    const first = await job.runOnce(new Date('2026-04-05T10:00:00.000Z'));
    await waitForStatus(syncJob, first.drifts[0].reconciliationEventId, 'completed');

    const second = await job.runOnce(new Date('2026-04-05T10:01:00.000Z'));

    assert.equal(second.driftCount, 1);
    assert.equal(second.queuedActions, 0);
    assert.equal(second.duplicateActions, 1);
    assert.equal(second.drifts[0].queuedAction, 'duplicate');
    assert.equal(published.length, 1);
  });
});

test('ReconciliationJob drift action uses workflow retry semantics when callback delivery transiently fails', async () => {
  await withTempDir(async (dir) => {
    let failuresRemaining = 1;
    const published: string[] = [];

    const publisher: BillingCallbackPublisher = {
      async publish(payload) {
        if (failuresRemaining > 0) {
          failuresRemaining -= 1;
          throw new Error('temporary provider callback failure');
        }

        published.push(payload.eventId);
      }
    };

    const syncJob = new SubscriptionSyncJob(publisher, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 3,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const { providerSource, internalSource } = buildStateSources();
    const job = new ReconciliationJob(providerSource, internalSource, syncJob);

    const result = await job.runOnce(new Date('2026-04-05T10:00:00.000Z'));
    await waitForStatus(syncJob, result.drifts[0].reconciliationEventId, 'completed');

    const snapshot = await syncJob.snapshotQueue();
    assert.equal(snapshot[0].status, 'completed');
    assert.equal(snapshot[0].attempts, 2);
    assert.equal(published.length, 1);
  });
});
