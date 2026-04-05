import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { ReconciliationJob } from '../src/jobs/reconciliationJob.ts';
import { SubscriptionSyncJob } from '../src/jobs/subscriptionSyncJob.ts';

test('ReconciliationJob classifies missing and plan-mismatch drift reasons deterministically', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'billing-recon-matrix-'));

  try {
    const syncJob = new SubscriptionSyncJob(undefined, {
      storagePath: join(dir, 'state.json'),
      maxAttempts: 2,
      initialBackoffMs: 1,
      maxBackoffMs: 5
    });

    const providerSource = {
      async listSubscriptions() {
        return [
          {
            tenantId: '00000000-0000-0000-0000-000000000001',
            subscriptionId: '00000000-0000-0000-0000-000000000011',
            status: 'active',
            planId: 'plan-pro'
          },
          {
            tenantId: '00000000-0000-0000-0000-000000000001',
            subscriptionId: '00000000-0000-0000-0000-000000000012',
            status: 'active',
            planId: 'plan-pro'
          }
        ];
      }
    };

    const internalSource = {
      async listSubscriptions() {
        return [
          {
            tenantId: '00000000-0000-0000-0000-000000000001',
            subscriptionId: '00000000-0000-0000-0000-000000000012',
            status: 'active',
            planId: 'plan-free'
          },
          {
            tenantId: '00000000-0000-0000-0000-000000000001',
            subscriptionId: '00000000-0000-0000-0000-000000000013',
            status: 'active',
            planId: 'plan-pro'
          }
        ];
      }
    };

    const job = new ReconciliationJob(providerSource, internalSource, syncJob);
    const result = await job.runOnce(new Date('2026-04-05T10:00:00.000Z'));
    const reasons = result.drifts.map((drift) => drift.reason).sort();

    assert.equal(result.driftCount, 3);
    assert.deepEqual(reasons, ['missing_internal_subscription', 'missing_provider_subscription', 'plan_mismatch']);
    assert.equal(result.queuedActions, 3);
    assert.equal(result.duplicateActions, 0);

    for (let i = 0; i < 50; i += 1) {
      const snapshot = await syncJob.snapshotQueue();
      if (snapshot.length === 3 && snapshot.every((item) => item.status !== 'processing')) {
        break;
      }

      await new Promise((resolve) => setTimeout(resolve, 5));
    }

    await new Promise((resolve) => setTimeout(resolve, 25));
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});
