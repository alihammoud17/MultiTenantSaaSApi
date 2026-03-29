import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { InternalSubscriptionEvent } from '../shared/types.ts';

export type WorkflowItemStatus = 'queued' | 'processing' | 'completed' | 'dead_letter';

export interface WorkflowItem {
  eventId: string;
  status: WorkflowItemStatus;
  attempts: number;
  nextAttemptAtUtc: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastError?: string;
  completedAtUtc?: string;
  deadLetterReason?: string;
  event: InternalSubscriptionEvent;
}

export interface WorkflowQueue {
  enqueue(event: InternalSubscriptionEvent): Promise<{ status: 'queued' | 'duplicate'; item: WorkflowItem }>;
  claimNextReady(now: Date): Promise<WorkflowItem | undefined>;
  markCompleted(eventId: string, now: Date): Promise<WorkflowItem | undefined>;
  markForRetry(eventId: string, retryAtUtc: string, error: string, now: Date): Promise<WorkflowItem | undefined>;
  markDeadLetter(eventId: string, reason: string, now: Date): Promise<WorkflowItem | undefined>;
  snapshot(): Promise<WorkflowItem[]>;
}

interface WorkflowQueueState {
  items: WorkflowItem[];
}

async function fileExists(path: string): Promise<boolean> {
  try {
    await readFile(path, 'utf8');
    return true;
  } catch {
    return false;
  }
}

export class FileWorkflowQueue implements WorkflowQueue {
  private readonly storagePath: string;
  private pending = Promise.resolve();

  public constructor(storagePath: string) {
    this.storagePath = storagePath;
  }

  public enqueue(event: InternalSubscriptionEvent): Promise<{ status: 'queued' | 'duplicate'; item: WorkflowItem }> {
    return this.withLock(async () => {
      const state = await this.loadState();
      const existing = state.items.find((item) => item.eventId === event.eventId);
      if (existing) {
        return { status: 'duplicate', item: existing };
      }

      const now = new Date().toISOString();
      const created: WorkflowItem = {
        eventId: event.eventId,
        status: 'queued',
        attempts: 0,
        nextAttemptAtUtc: now,
        createdAtUtc: now,
        updatedAtUtc: now,
        event
      };

      state.items.push(created);
      await this.saveState(state);
      return { status: 'queued', item: created };
    });
  }

  public claimNextReady(now: Date): Promise<WorkflowItem | undefined> {
    return this.withLock(async () => {
      const state = await this.loadState();
      const readyItem = state.items.find((item) => item.status === 'queued' && item.nextAttemptAtUtc <= now.toISOString());
      if (!readyItem) {
        return undefined;
      }

      readyItem.status = 'processing';
      readyItem.attempts += 1;
      readyItem.updatedAtUtc = now.toISOString();
      await this.saveState(state);
      return readyItem;
    });
  }

  public markCompleted(eventId: string, now: Date): Promise<WorkflowItem | undefined> {
    return this.withLock(async () => {
      const state = await this.loadState();
      const item = state.items.find((entry) => entry.eventId === eventId);
      if (!item) {
        return undefined;
      }

      item.status = 'completed';
      item.completedAtUtc = now.toISOString();
      item.updatedAtUtc = now.toISOString();
      item.lastError = undefined;
      await this.saveState(state);
      return item;
    });
  }

  public markForRetry(eventId: string, retryAtUtc: string, error: string, now: Date): Promise<WorkflowItem | undefined> {
    return this.withLock(async () => {
      const state = await this.loadState();
      const item = state.items.find((entry) => entry.eventId === eventId);
      if (!item) {
        return undefined;
      }

      item.status = 'queued';
      item.lastError = error;
      item.nextAttemptAtUtc = retryAtUtc;
      item.updatedAtUtc = now.toISOString();
      await this.saveState(state);
      return item;
    });
  }

  public markDeadLetter(eventId: string, reason: string, now: Date): Promise<WorkflowItem | undefined> {
    return this.withLock(async () => {
      const state = await this.loadState();
      const item = state.items.find((entry) => entry.eventId === eventId);
      if (!item) {
        return undefined;
      }

      item.status = 'dead_letter';
      item.deadLetterReason = reason;
      item.lastError = reason;
      item.updatedAtUtc = now.toISOString();
      await this.saveState(state);
      return item;
    });
  }

  public snapshot(): Promise<WorkflowItem[]> {
    return this.withLock(async () => {
      const state = await this.loadState();
      return state.items.map((item) => ({ ...item }));
    });
  }

  private async loadState(): Promise<WorkflowQueueState> {
    await mkdir(dirname(this.storagePath), { recursive: true });

    if (!(await fileExists(this.storagePath))) {
      return { items: [] };
    }

    const raw = await readFile(this.storagePath, 'utf8');
    if (!raw.trim()) {
      return { items: [] };
    }

    return JSON.parse(raw) as WorkflowQueueState;
  }

  private async saveState(state: WorkflowQueueState): Promise<void> {
    await mkdir(dirname(this.storagePath), { recursive: true });
    await writeFile(this.storagePath, JSON.stringify(state, null, 2));
  }

  private withLock<T>(work: () => Promise<T>): Promise<T> {
    const operation = this.pending.then(work, work);
    this.pending = operation.then(() => undefined, () => undefined);
    return operation;
  }
}
