import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { BillingMetrics } from '../src/observability/metrics.ts';
import { beginObservedRequest } from '../src/observability/requestContext.ts';
import { createApp } from '../src/app.ts';
import { WorkflowWorker } from '../src/workflows/workflowWorker.ts';
import type { RetryPolicy } from '../src/workflows/retryPolicy.ts';
import type { WorkflowItem, WorkflowQueue } from '../src/workflows/workflowQueue.ts';
import type { InternalSubscriptionEvent } from '../src/shared/types.ts';

function parseLogLine(line: unknown): Record<string, unknown> {
  assert.equal(typeof line, 'string');
  return JSON.parse(line);
}

function assertNoUnsafeDiagnostics(value: unknown): void {
  const serialized = JSON.stringify(value).toLowerCase();
  assert.equal(serialized.includes('bearer ey'), false);
  assert.equal(serialized.includes('sk_live_'), false);
  assert.equal(serialized.includes('callback-secret-'), false);
  assert.equal(serialized.includes('tok_'), false);
}

function makeWorkflowItem(overrides: Partial<WorkflowItem> = {}): WorkflowItem {
  const event: InternalSubscriptionEvent = {
    eventId: 'evt_workflow_quality_gate',
    eventType: 'subscription.renewed',
    provider: 'stripe',
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    occurredAt: '2026-04-01T00:00:00.000Z',
    effectiveAt: '2026-05-01T00:00:00.000Z',
    correlationId: 'corr_workflow_quality_gate',
    payload: {}
  };

  return {
    eventId: event.eventId,
    status: 'queued',
    attempts: 1,
    nextAttemptAtUtc: '2026-04-01T00:00:00.000Z',
    createdAtUtc: '2026-04-01T00:00:00.000Z',
    updatedAtUtc: '2026-04-01T00:00:00.000Z',
    event,
    ...overrides
  };
}

test('request observability logs include required safe structured fields', () => {
  const captured: string[] = [];
  const originalInfo = console.info;
  console.info = (line?: unknown) => {
    captured.push(String(line ?? ''));
  };

  try {
    const metrics = new BillingMetrics();
    const observer = beginObservedRequest(metrics, 'GET', '/metrics', 'corr-quality-gate-1');
    observer.finish(200);
  } finally {
    console.info = originalInfo;
  }

  assert.equal(captured.length, 2);

  const started = parseLogLine(captured[0]);
  assert.equal(started.event, 'http.request.started');
  assert.equal(started.correlationId, 'corr-quality-gate-1');
  assert.equal(started.method, 'GET');
  assert.equal(started.route, '/metrics');
  assert.equal(typeof started.traceId, 'string');
  assert.equal(typeof started.timestamp, 'string');

  const completed = parseLogLine(captured[1]);
  assert.equal(completed.event, 'http.request.completed');
  assert.equal(completed.correlationId, 'corr-quality-gate-1');
  assert.equal(completed.method, 'GET');
  assert.equal(completed.route, '/metrics');
  assert.equal(completed.statusCode, 200);
  assert.equal(typeof completed.durationMs, 'number');
  assert.equal(typeof completed.traceId, 'string');
  assert.equal(typeof completed.timestamp, 'string');
});

test('POST /webhooks/provider preserves incoming correlation id in headers and body', async () => {
  process.env.BILLING_PROVIDER = 'placeholder';
  const { server } = createApp();
  server.listen(0);
  await once(server, 'listening');

  const address = server.address();
  if (!address || typeof address === 'string') {
    throw new Error('Expected TCP server address');
  }

  try {
    const response = await fetch(`http://127.0.0.1:${address.port}/webhooks/provider`, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'x-correlation-id': 'corr-webhook-quality-gate'
      },
      body: JSON.stringify({ type: 'subscription.updated' })
    });

    const body = await response.json() as Record<string, unknown>;

    assert.equal(response.status, 202);
    assert.equal(response.headers.get('x-correlation-id'), 'corr-webhook-quality-gate');
    assert.equal(body.correlationId, 'corr-webhook-quality-gate');
    assert.equal(typeof body.traceId, 'string');
    assert.equal(typeof body.reason, 'string');
  } finally {
    server.close();
    await once(server, 'close');
  }
});

class SingleItemQueue implements WorkflowQueue {
  private item: WorkflowItem | undefined;

  public constructor(item: WorkflowItem) {
    this.item = item;
  }

  public async enqueue(_event: InternalSubscriptionEvent): Promise<{ status: 'queued' | 'duplicate'; item: WorkflowItem }> {
    throw new Error('not used');
  }

  public async claimNextReady(_now: Date): Promise<WorkflowItem | undefined> {
    if (!this.item || this.item.status !== 'queued') {
      return undefined;
    }

    this.item.status = 'processing';
    return this.item;
  }

  public async markCompleted(_eventId: string, _now: Date): Promise<WorkflowItem | undefined> {
    if (!this.item) {
      return undefined;
    }

    this.item.status = 'completed';
    return this.item;
  }

  public async markForRetry(eventId: string, retryAtUtc: string, error: string, _now: Date): Promise<WorkflowItem | undefined> {
    if (!this.item || this.item.eventId !== eventId) {
      return undefined;
    }

    this.item.status = 'queued';
    this.item.lastError = error;
    this.item.nextAttemptAtUtc = retryAtUtc;
    return this.item;
  }

  public async markDeadLetter(eventId: string, reason: string, _now: Date): Promise<WorkflowItem | undefined> {
    if (!this.item || this.item.eventId !== eventId) {
      return undefined;
    }

    this.item.status = 'dead_letter';
    this.item.deadLetterReason = reason;
    this.item.lastError = reason;
    return this.item;
  }

  public async snapshot(): Promise<WorkflowItem[]> {
    return this.item ? [this.item] : [];
  }
}

test('workflow retry/dead-letter logs enforce required safe structured fields', async () => {
  const warnings: string[] = [];
  const errors: string[] = [];

  const originalWarn = console.warn;
  const originalError = console.error;
  console.warn = (line?: unknown) => {
    warnings.push(String(line ?? ''));
  };
  console.error = (line?: unknown) => {
    errors.push(String(line ?? ''));
  };

  try {
    const queue = new SingleItemQueue(makeWorkflowItem());
    const retryPolicy: RetryPolicy = {
      next() {
        return {
          shouldRetry: false
        };
      }
    };

    const worker = new WorkflowWorker(queue, {
      async process() {
        throw new Error('downstream_outage');
      }
    }, retryPolicy, 10_000);

    await worker.processUntilEmpty();
  } finally {
    console.warn = originalWarn;
    console.error = originalError;
  }

  assert.equal(warnings.length, 0);
  assert.equal(errors.length, 1);

  const deadLettered = parseLogLine(errors[0]);
  assert.equal(deadLettered.event, 'workflow-worker.dead-lettered');
  assert.equal(deadLettered.eventId, 'evt_workflow_quality_gate');
  assert.equal(deadLettered.correlationId, 'corr_workflow_quality_gate');
  assert.equal(deadLettered.tenantId, '00000000-0000-0000-0000-000000000001');
  assert.equal(deadLettered.status, 'dead_letter');
  assert.equal(deadLettered.message, 'downstream_outage');
  assert.equal(typeof deadLettered.attempts, 'number');
  assert.equal(typeof deadLettered.timestamp, 'string');
});

test('workflow retry-scheduled diagnostics preserve safe context and sanitize sensitive values on transient failures', async () => {
  const warnings: string[] = [];
  const errors: string[] = [];
  const queue = new SingleItemQueue(makeWorkflowItem({ eventId: 'evt_transient_quality_gate' }));

  const originalWarn = console.warn;
  const originalError = console.error;
  console.warn = (line?: unknown) => {
    warnings.push(String(line ?? ''));
  };
  console.error = (line?: unknown) => {
    errors.push(String(line ?? ''));
  };

  try {
    let retryDecisions = 0;
    const retryPolicy: RetryPolicy = {
      next() {
        retryDecisions += 1;
        if (retryDecisions === 1) {
          return {
            shouldRetry: true,
            retryAtUtc: '2026-04-01T00:00:10.000Z'
          };
        }

        return {
          shouldRetry: false
        };
      }
    };

    const worker = new WorkflowWorker(queue, {
      async process() {
        throw new Error('temporary outage authorization=Bearer eyJhbGciOiJIUzI1NiJ9 callback-secret=callback-secret-123 token=tok_abc');
      }
    }, retryPolicy, 10_000);

    await worker.processUntilEmpty();
  } finally {
    console.warn = originalWarn;
    console.error = originalError;
  }

  assert.equal(errors.length, 1);
  assert.equal(warnings.length, 1);

  const retryScheduled = parseLogLine(warnings[0]);
  assert.equal(retryScheduled.event, 'workflow-worker.retry-scheduled');
  assert.equal(retryScheduled.eventId, 'evt_transient_quality_gate');
  assert.equal(retryScheduled.correlationId, 'corr_workflow_quality_gate');
  assert.equal(retryScheduled.status, 'retry_scheduled');
  assert.equal(retryScheduled.retryAtUtc, '2026-04-01T00:00:10.000Z');
  assert.equal(typeof retryScheduled.attempts, 'number');
  assert.equal(typeof retryScheduled.timestamp, 'string');
  assertNoUnsafeDiagnostics(retryScheduled);

  const snapshot = await queue.snapshot();
  assert.equal(snapshot[0].status, 'dead_letter');
  assert.equal(typeof snapshot[0].lastError, 'string');
  assertNoUnsafeDiagnostics(snapshot[0].lastError);
});
