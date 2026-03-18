export type BillingProvider = 'placeholder' | 'stripe' | 'paddle';

export interface BillingServiceConfig {
  port: number;
  nodeEnv: string;
  provider: BillingProvider;
  webhookSigningSecret?: string;
  callbackBaseUrl?: string;
}

export interface InternalSubscriptionEvent {
  eventId: string;
  eventType: 'subscription.updated' | 'subscription.canceled' | 'invoice.payment_failed';
  provider: BillingProvider;
  tenantId: string;
  subscriptionId: string;
  occurredAt: string;
  correlationId: string;
  payload: Record<string, unknown>;
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
