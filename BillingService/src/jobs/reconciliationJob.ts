import { logger } from '../observability/logger.ts';
import type { WorkflowQueue } from '../workflows/workflowQueue.ts';

export interface ReconciliationJobResult {
  generatedAtUtc: string;
  pendingCount: number;
  deadLetterCount: number;
  oldestPendingAgeSeconds: number | null;
}

export class ReconciliationJob {
  private readonly queue: WorkflowQueue;

  public constructor(queue: WorkflowQueue) {
    this.queue = queue;
  }

  public async runOnce(now: Date = new Date()): Promise<ReconciliationJobResult> {
    const snapshot = await this.queue.snapshot();
    const pending = snapshot.filter((item) => item.status === 'queued' || item.status === 'processing');
    const deadLetters = snapshot.filter((item) => item.status === 'dead_letter');

    const oldestPendingMillis = pending
      .map((item) => now.getTime() - new Date(item.createdAtUtc).getTime())
      .sort((left, right) => right - left)[0];

    const result: ReconciliationJobResult = {
      generatedAtUtc: now.toISOString(),
      pendingCount: pending.length,
      deadLetterCount: deadLetters.length,
      oldestPendingAgeSeconds: typeof oldestPendingMillis === 'number' ? Math.max(0, Math.floor(oldestPendingMillis / 1000)) : null
    };

    logger.info('reconciliation-job.summary', { ...result });
    return result;
  }
}
