import { logger } from '../observability/logger.ts';
import type {
  ProviderInvoiceRecord,
  TenantBillingProviderGateway,
  TenantCheckoutSessionRequest,
  TenantHostedSession,
  TenantInvoiceSyncRequest,
  TenantPortalSessionRequest
} from './tenantBillingGateway.ts';

interface StripeGatewayConfig {
  apiKey: string;
  apiBaseUrl: string;
}

type FetchLike = (input: string, init?: { method?: string; headers?: Record<string, string>; body?: string }) => Promise<{
  status: number;
  json(): Promise<any>;
}>;

interface StripeSessionResponse {
  id?: string;
  url?: string;
  expires_at?: number;
}

interface StripeInvoiceListResponse {
  data?: StripeInvoice[];
}

interface StripeInvoice {
  id?: string;
  customer?: string;
  subscription?: string;
  status?: string;
  amount_due?: number;
  amount_paid?: number;
  currency?: string;
  created?: number;
  metadata?: Record<string, string | undefined>;
  lines?: {
    data?: Array<{
      period?: {
        start?: number;
        end?: number;
      };
    }>;
  };
}

function unixToIso(value: number | undefined): string | undefined {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) {
    return undefined;
  }

  return new Date(value * 1000).toISOString();
}

function safeNumber(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return 0;
  }

  return value;
}

function toFormBody(values: Record<string, string>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    params.append(key, value);
  }

  return params.toString();
}

export class StripeTenantBillingGateway implements TenantBillingProviderGateway {
  private readonly apiKey: string;
  private readonly apiBaseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(config: StripeGatewayConfig, fetchFn: FetchLike = fetch) {
    if (!config.apiKey) {
      throw new Error('STRIPE_API_KEY is required for StripeTenantBillingGateway.');
    }

    this.apiKey = config.apiKey;
    this.apiBaseUrl = config.apiBaseUrl.replace(/\/$/, '');
    this.fetchFn = fetchFn;
  }

  public async createCheckoutSession(request: TenantCheckoutSessionRequest): Promise<TenantHostedSession> {
    const payload = toFormBody({
      mode: 'subscription',
      customer: request.providerCustomerId,
      success_url: request.successUrl,
      cancel_url: request.cancelUrl,
      'line_items[0][price]': request.providerPriceId,
      'line_items[0][quantity]': '1',
      'metadata[tenant_id]': request.tenantId,
      'metadata[subscription_id]': request.subscriptionId,
      'metadata[correlation_id]': request.correlationId
    });

    const session = await this.postForm<StripeSessionResponse>('/v1/checkout/sessions', payload, request.correlationId);

    if (!session.id || !session.url) {
      throw new Error('Stripe checkout session response missing required fields.');
    }

    return {
      providerSessionId: session.id,
      url: session.url,
      expiresAtUtc: unixToIso(session.expires_at)
    };
  }

  public async createPortalSession(request: TenantPortalSessionRequest): Promise<TenantHostedSession> {
    const payload = toFormBody({
      customer: request.providerCustomerId,
      return_url: request.returnUrl
    });

    const session = await this.postForm<StripeSessionResponse>('/v1/billing_portal/sessions', payload, request.correlationId);

    if (!session.id || !session.url) {
      throw new Error('Stripe portal session response missing required fields.');
    }

    return {
      providerSessionId: session.id,
      url: session.url
    };
  }

  public async listInvoicesForSync(request: TenantInvoiceSyncRequest): Promise<ProviderInvoiceRecord[]> {
    const limit = Math.max(1, Math.min(100, request.limit ?? 25));
    const query = new URLSearchParams({
      customer: request.providerCustomerId,
      subscription: request.providerSubscriptionId,
      limit: String(limit)
    });

    const list = await this.getJson<StripeInvoiceListResponse>(`/v1/invoices?${query.toString()}`, request.correlationId);
    const invoices = Array.isArray(list.data) ? list.data : [];

    return invoices
      .map((invoice) => this.mapInvoice(invoice, request))
      .filter((invoice): invoice is ProviderInvoiceRecord => invoice !== undefined);
  }

  private mapInvoice(invoice: StripeInvoice, request: TenantInvoiceSyncRequest): ProviderInvoiceRecord | undefined {
    const invoiceId = invoice.id;
    const customerId = invoice.customer;
    const subscriptionId = invoice.subscription;
    const metadataTenantId = invoice.metadata?.tenant_id;
    const metadataSubscriptionId = invoice.metadata?.subscription_id;

    if (!invoiceId || !customerId || !subscriptionId) {
      logger.warn('stripe.gateway.invoice-skipped.missing-fields', {
        correlationId: request.correlationId,
        providerInvoiceId: invoiceId,
        tenantId: request.tenantId,
        subscriptionId: request.subscriptionId
      });
      return undefined;
    }

    if (metadataTenantId !== request.tenantId || metadataSubscriptionId !== request.subscriptionId) {
      logger.warn('stripe.gateway.invoice-skipped.tenant-mismatch', {
        correlationId: request.correlationId,
        providerInvoiceId: invoiceId,
        expectedTenantId: request.tenantId,
        expectedSubscriptionId: request.subscriptionId,
        metadataTenantId,
        metadataSubscriptionId
      });
      return undefined;
    }

    const firstLine = invoice.lines?.data?.[0];

    return {
      providerInvoiceId: invoiceId,
      providerEventId: invoiceId,
      providerCustomerId: customerId,
      providerSubscriptionId: subscriptionId,
      tenantId: metadataTenantId,
      subscriptionId: metadataSubscriptionId,
      status: invoice.status ?? 'unknown',
      amountDue: safeNumber(invoice.amount_due),
      amountPaid: safeNumber(invoice.amount_paid),
      currency: (invoice.currency ?? 'usd').toUpperCase(),
      periodStartUtc: unixToIso(firstLine?.period?.start),
      periodEndUtc: unixToIso(firstLine?.period?.end),
      occurredAtUtc: unixToIso(invoice.created) ?? new Date().toISOString()
    };
  }

  private async postForm<T>(path: string, body: string, correlationId: string): Promise<T> {
    const response = await this.fetchFn(`${this.apiBaseUrl}${path}`, {
      method: 'POST',
      headers: this.buildHeaders(),
      body
    });

    return this.readResponse<T>(response, path, correlationId);
  }

  private async getJson<T>(path: string, correlationId: string): Promise<T> {
    const response = await this.fetchFn(`${this.apiBaseUrl}${path}`, {
      method: 'GET',
      headers: {
        authorization: `Bearer ${this.apiKey}`
      }
    });

    return this.readResponse<T>(response, path, correlationId);
  }

  private async readResponse<T>(response: { status: number; json(): Promise<any> }, path: string, correlationId: string): Promise<T> {
    const payload = await response.json();

    if (response.status >= 200 && response.status < 300) {
      return payload as T;
    }

    const errorMessage = typeof payload?.error?.message === 'string'
      ? payload.error.message
      : `Stripe API request failed for ${path}`;

    logger.error('stripe.gateway.request-failed', {
      correlationId,
      path,
      status: response.status,
      errorMessage
    });

    throw new Error(errorMessage);
  }

  private buildHeaders(): Record<string, string> {
    return {
      authorization: `Bearer ${this.apiKey}`,
      'content-type': 'application/x-www-form-urlencoded'
    };
  }
}
