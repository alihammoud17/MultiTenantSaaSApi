import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { FileWorkflowQueue } from '../src/workflows/workflowQueue.ts';

test('FileWorkflowQueue recovers queued items across restart and preserves retry metadata', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'billing-queue-recovery-'));

  try {
    const path = join(dir, 'state.json');
    const initialQueue = new FileWorkflowQueue(path);
    await initialQueue.enqueue({
      eventId: 'evt_recovery',
      eventType: 'subscription.renewed',
      provider: 'stripe',
      tenantId: '00000000-0000-0000-0000-000000000001',
      subscriptionId: '00000000-0000-0000-0000-000000000002',
      occurredAt: '2026-03-18T12:00:00.000Z',
      correlationId: 'corr_recovery',
      payload: {}
    });

    await initialQueue.markForRetry(
      'evt_recovery',
      '2026-03-18T12:00:10.000Z',
      'temporary failure',
      new Date('2026-03-18T12:00:00.000Z')
    );

    const restartedQueue = new FileWorkflowQueue(path);
    const snapshot = await restartedQueue.snapshot();

    assert.equal(snapshot.length, 1);
    assert.equal(snapshot[0].eventId, 'evt_recovery');
    assert.equal(snapshot[0].status, 'queued');
    assert.equal(snapshot[0].lastError, 'temporary failure');
    assert.equal(snapshot[0].nextAttemptAtUtc, '2026-03-18T12:00:10.000Z');
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test('FileWorkflowQueue treats whitespace-only state files as empty state', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'billing-queue-whitespace-'));

  try {
    const path = join(dir, 'state.json');
    await writeFile(path, '   \n   ');

    const queue = new FileWorkflowQueue(path);
    const snapshot = await queue.snapshot();

    assert.deepEqual(snapshot, []);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

