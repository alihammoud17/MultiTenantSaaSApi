import { logger } from '../observability/logger.ts';
import type { InternalSubscriptionEventType } from '../shared/types.ts';
import type { SubscriptionSyncJob } from './subscriptionSyncJob.ts';

export interface ProviderSubscriptionState {
  tenantId: string;
  subscriptionId: string;
  status: string;
  planId?: string;
}

export interface InternalSubscriptionState {
  tenantId: string;
  subscriptionId: string;
  status: string;
  planId?: string;
}

export interface ProviderStateSource {
  listSubscriptions(): Promise<ProviderSubscriptionState[]>;
}

export interface InternalStateSource {
  listSubscriptions(): Promise<InternalSubscriptionState[]>;
}

export interface DriftRecord {
  tenantId: string;
  subscriptionId: string;
  reason: 'missing_internal_subscription' | 'missing_provider_subscription' | 'status_mismatch' | 'plan_mismatch';
  providerStatus?: string;
  internalStatus?: string;
  providerPlanId?: string;
  internalPlanId?: string;
  reconciliationEventId: string;
  queuedAction: 'queued' | 'duplicate';
}

export interface ReconciliationJobResult {
  generatedAtUtc: string;
  providerCount: number;
  internalCount: number;
  comparedCount: number;
  inSyncCount: number;
  driftCount: number;
  queuedActions: number;
  duplicateActions: number;
  drifts: DriftRecord[];
}

interface DriftClassification {
  reason: DriftRecord['reason'];
  eventType: InternalSubscriptionEventType;
}

function toKey(tenantId: string, subscriptionId: string): string {
  return `${tenantId}:${subscriptionId}`;
}

function normalizeStatus(status: string): string {
  return status.trim().toLowerCase();
}

function mapProviderStatusToEventType(status: string): InternalSubscriptionEventType {
  switch (normalizeStatus(status)) {
    case 'active':
      return 'subscription.activated';
    case 'past_due':
    case 'grace_period':
      return 'invoice.payment_failed';
    case 'canceled':
      return 'subscription.canceled';
    case 'expired':
      return 'subscription.expired';
    default:
      return 'subscription.renewed';
  }
}

function detectDrift(provider: ProviderSubscriptionState | undefined, internal: InternalSubscriptionState | undefined): DriftClassification | undefined {
  if (provider && !internal) {
    return {
      reason: 'missing_internal_subscription',
      eventType: mapProviderStatusToEventType(provider.status)
    };
  }

  if (!provider && internal) {
    return {
      reason: 'missing_provider_subscription',
      eventType: 'subscription.expired'
    };
  }

  if (!provider || !internal) {
    return undefined;
  }

  if (normalizeStatus(provider.status) !== normalizeStatus(internal.status)) {
    return {
      reason: 'status_mismatch',
      eventType: mapProviderStatusToEventType(provider.status)
    };
  }

  if (provider.planId && internal.planId && provider.planId !== internal.planId) {
    return {
      reason: 'plan_mismatch',
      eventType: 'subscription.plan_changed'
    };
  }

  return undefined;
}

function buildReconciliationEventId(provider: ProviderSubscriptionState | undefined, internal: InternalSubscriptionState | undefined, reason: DriftRecord['reason']): string {
  const parts = [
    provider?.tenantId ?? internal?.tenantId ?? 'unknown_tenant',
    provider?.subscriptionId ?? internal?.subscriptionId ?? 'unknown_subscription',
    reason,
    provider?.status ?? 'none',
    internal?.status ?? 'none',
    provider?.planId ?? 'none',
    internal?.planId ?? 'none'
  ];

  return `recon_${parts.join('__').replace(/[^a-zA-Z0-9_]/g, '_').slice(0, 96)}`;
}

export class ReconciliationJob {
  private readonly providerSource: ProviderStateSource;
  private readonly internalSource: InternalStateSource;
  private readonly syncJob: SubscriptionSyncJob;

  public constructor(providerSource: ProviderStateSource, internalSource: InternalStateSource, syncJob: SubscriptionSyncJob) {
    this.providerSource = providerSource;
    this.internalSource = internalSource;
    this.syncJob = syncJob;
  }

  public async runOnce(now: Date = new Date()): Promise<ReconciliationJobResult> {
    const [providerSubscriptions, internalSubscriptions] = await Promise.all([
      this.providerSource.listSubscriptions(),
      this.internalSource.listSubscriptions()
    ]);

    const providerByKey = new Map(providerSubscriptions.map((subscription) => [toKey(subscription.tenantId, subscription.subscriptionId), subscription]));
    const internalByKey = new Map(internalSubscriptions.map((subscription) => [toKey(subscription.tenantId, subscription.subscriptionId), subscription]));

    const keys = new Set<string>([...providerByKey.keys(), ...internalByKey.keys()]);

    const drifts: DriftRecord[] = [];
    let inSyncCount = 0;

    for (const key of keys) {
      const provider = providerByKey.get(key);
      const internal = internalByKey.get(key);
      const drift = detectDrift(provider, internal);

      if (!drift) {
        inSyncCount += 1;
        continue;
      }

      const eventId = buildReconciliationEventId(provider, internal, drift.reason);
      const tenantId = provider?.tenantId ?? internal?.tenantId ?? '';
      const subscriptionId = provider?.subscriptionId ?? internal?.subscriptionId ?? '';
      const occurredAt = now.toISOString();

      const queued = await this.syncJob.enqueue({
        eventId,
        eventType: drift.eventType,
        provider: 'placeholder',
        tenantId,
        subscriptionId,
        occurredAt,
        targetPlanId: provider?.planId,
        correlationId: `reconcile-${eventId}`,
        payload: {
          source: 'reconciliation_job',
          driftReason: drift.reason,
          providerStatus: provider?.status,
          internalStatus: internal?.status,
          providerPlanId: provider?.planId,
          internalPlanId: internal?.planId
        }
      });

      const driftRecord: DriftRecord = {
        tenantId,
        subscriptionId,
        reason: drift.reason,
        providerStatus: provider?.status,
        internalStatus: internal?.status,
        providerPlanId: provider?.planId,
        internalPlanId: internal?.planId,
        reconciliationEventId: eventId,
        queuedAction: queued.status
      };

      drifts.push(driftRecord);

      logger.warn('reconciliation-job.drift-detected', { ...driftRecord });
    }

    const queuedActions = drifts.filter((drift) => drift.queuedAction === 'queued').length;
    const duplicateActions = drifts.length - queuedActions;

    const result: ReconciliationJobResult = {
      generatedAtUtc: now.toISOString(),
      providerCount: providerSubscriptions.length,
      internalCount: internalSubscriptions.length,
      comparedCount: keys.size,
      inSyncCount,
      driftCount: drifts.length,
      queuedActions,
      duplicateActions,
      drifts
    };

    logger.info('reconciliation-job.summary', {
      generatedAtUtc: result.generatedAtUtc,
      providerCount: result.providerCount,
      internalCount: result.internalCount,
      comparedCount: result.comparedCount,
      inSyncCount: result.inSyncCount,
      driftCount: result.driftCount,
      queuedActions: result.queuedActions,
      duplicateActions: result.duplicateActions
    });

    return result;
  }
}
