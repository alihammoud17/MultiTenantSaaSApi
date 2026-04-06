export interface TenantCheckoutSessionRequest {
  tenantId: string;
  subscriptionId: string;
  providerCustomerId: string;
  providerPriceId: string;
  successUrl: string;
  cancelUrl: string;
  correlationId: string;
}

export interface TenantPortalSessionRequest {
  tenantId: string;
  subscriptionId: string;
  providerCustomerId: string;
  returnUrl: string;
  correlationId: string;
}

export interface TenantInvoiceSyncRequest {
  tenantId: string;
  subscriptionId: string;
  providerCustomerId: string;
  providerSubscriptionId: string;
  correlationId: string;
  limit?: number;
}

export interface TenantHostedSession {
  providerSessionId: string;
  url: string;
  expiresAtUtc?: string;
}

export interface ProviderInvoiceRecord {
  providerInvoiceId: string;
  providerEventId: string;
  providerCustomerId: string;
  providerSubscriptionId: string;
  tenantId: string;
  subscriptionId: string;
  status: string;
  amountDue: number;
  amountPaid: number;
  currency: string;
  periodStartUtc?: string;
  periodEndUtc?: string;
  occurredAtUtc: string;
}

export interface TenantBillingProviderGateway {
  createCheckoutSession(request: TenantCheckoutSessionRequest): Promise<TenantHostedSession>;
  createPortalSession(request: TenantPortalSessionRequest): Promise<TenantHostedSession>;
  listInvoicesForSync(request: TenantInvoiceSyncRequest): Promise<ProviderInvoiceRecord[]>;
}
