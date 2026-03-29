export type BillingProvider = 'placeholder' | 'stripe' | 'paddle';

export type InternalSubscriptionEventType =
  | 'subscription.activated'
  | 'subscription.renewed'
  | 'subscription.plan_changed'
  | 'subscription.downgrade_scheduled'
  | 'subscription.grace_period_started'
  | 'subscription.grace_period_expired'
  | 'subscription.canceled'
  | 'subscription.expired'
  | 'invoice.payment_failed';

export interface BillingServiceConfig {
  port: number;
  nodeEnv: string;
  provider: BillingProvider;
  serviceName: string;
  webhookSigningSecret?: string;
  callbackBaseUrl?: string;
  workflowStatePath: string;
  workflowMaxAttempts: number;
  workflowInitialBackoffMs: number;
  workflowMaxBackoffMs: number;
  workflowPollIntervalMs: number;
  reconciliationIntervalMs: number;
}

export interface InternalSubscriptionEvent {
  eventId: string;
  eventType: InternalSubscriptionEventType;
  provider: BillingProvider;
  tenantId: string;
  subscriptionId: string;
  occurredAt: string;
  effectiveAt?: string;
  targetPlanId?: string;
  correlationId: string;
  payload: Record<string, unknown>;
}

export interface BillingCallbackPayload {
  contractVersion: '2026-03-18';
  eventId: string;
  eventType: InternalSubscriptionEventType;
  provider: BillingProvider;
  providerEventId: string;
  tenantId: string;
  subscriptionId: string;
  targetPlanId?: string;
  occurredAtUtc: string;
  effectiveAtUtc?: string;
  correlationId: string;
}

export interface ProviderWebhookResult {
  accepted: boolean;
  reason?: string;
  normalizedEvent?: InternalSubscriptionEvent;
}

export interface BillingProviderAdapter {
  readonly name: BillingProvider;
  verifyAndNormalizeWebhook(input: {
    rawBody: string;
    signature?: string;
    headers: Record<string, string | string[] | undefined>;
  }): Promise<ProviderWebhookResult>;
}
