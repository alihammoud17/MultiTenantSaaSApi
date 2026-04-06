import test from 'node:test';
import assert from 'node:assert/strict';
import { StripeTenantBillingGateway } from '../src/providers/stripeTenantBillingGateway.ts';

async function expectMessage(promiseFactory: () => Promise<unknown>, messagePattern: RegExp): Promise<void> {
  let error: unknown;

  try {
    await promiseFactory();
  } catch (caught) {
    error = caught;
  }

  assert.match(String(error), messagePattern);
}

test('StripeTenantBillingGateway creates checkout and portal sessions with tenant-safe metadata', async () => {
  const calls: Array<{ input: string; init?: { method?: string; headers?: Record<string, string>; body?: string } }> = [];
  const responses = [
    {
      status: 200,
      async json() {
        return {
          id: 'cs_test_123',
          url: 'https://billing.example/checkout/cs_test_123',
          expires_at: 1770000000
        };
      }
    },
    {
      status: 200,
      async json() {
        return {
          id: 'bps_test_123',
          url: 'https://billing.example/portal/bps_test_123'
        };
      }
    }
  ];

  const gateway = new StripeTenantBillingGateway(
    {
      apiKey: 'sk_test_123',
      apiBaseUrl: 'https://api.stripe.com'
    },
    async (input, init) => {
      calls.push({ input, init });
      return responses.shift()!;
    }
  );

  const checkout = await gateway.createCheckoutSession({
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    providerCustomerId: 'cus_123',
    providerPriceId: 'price_123',
    successUrl: 'https://app.example/billing/success',
    cancelUrl: 'https://app.example/billing/cancel',
    correlationId: 'corr_checkout_1'
  });

  const portal = await gateway.createPortalSession({
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    providerCustomerId: 'cus_123',
    returnUrl: 'https://app.example/billing',
    correlationId: 'corr_portal_1'
  });

  assert.equal(calls.length, 2);
  assert.match(calls[0].input, /\/v1\/checkout\/sessions$/);
  assert.match(calls[0].init?.body ?? '', /metadata%5Btenant_id%5D=00000000-0000-0000-0000-000000000001/);
  assert.match(calls[0].init?.body ?? '', /metadata%5Bsubscription_id%5D=00000000-0000-0000-0000-000000000002/);
  assert.match(calls[1].input, /\/v1\/billing_portal\/sessions$/);
  assert.equal(checkout.providerSessionId, 'cs_test_123');
  assert.equal(portal.providerSessionId, 'bps_test_123');
});

test('StripeTenantBillingGateway surfaces clean errors for checkout/portal session failures', async () => {
  const gateway = new StripeTenantBillingGateway(
    {
      apiKey: 'sk_test_123',
      apiBaseUrl: 'https://api.stripe.com'
    },
    async (input) => {
      if (String(input).endsWith('/v1/checkout/sessions')) {
        return {
          status: 200,
          async json() {
            return {
              id: 'cs_missing_url'
            };
          }
        };
      }

      return {
        status: 400,
        async json() {
          return {
            error: {
              message: 'Portal session creation failed'
            }
          };
        }
      };
    }
  );

  await expectMessage(
    () => gateway.createCheckoutSession({
      tenantId: '00000000-0000-0000-0000-000000000001',
      subscriptionId: '00000000-0000-0000-0000-000000000002',
      providerCustomerId: 'cus_123',
      providerPriceId: 'price_123',
      successUrl: 'https://app.example/billing/success',
      cancelUrl: 'https://app.example/billing/cancel',
      correlationId: 'corr_checkout_error'
    }),
    /Stripe checkout session response missing required fields\./
  );

  await expectMessage(
    () => gateway.createPortalSession({
      tenantId: '00000000-0000-0000-0000-000000000001',
      subscriptionId: '00000000-0000-0000-0000-000000000002',
      providerCustomerId: 'cus_123',
      returnUrl: 'https://app.example/billing',
      correlationId: 'corr_portal_error'
    }),
    /Portal session creation failed/
  );
});

test('StripeTenantBillingGateway listInvoicesForSync filters invoices with invalid tenant mapping', async () => {
  const gateway = new StripeTenantBillingGateway(
    {
      apiKey: 'sk_test_123',
      apiBaseUrl: 'https://api.stripe.com'
    },
    async () => ({
      status: 200,
      async json() {
        return {
          data: [
            {
              id: 'in_valid',
              customer: 'cus_123',
              subscription: 'sub_123',
              status: 'open',
              amount_due: 2500,
              amount_paid: 0,
              currency: 'usd',
              created: 1760000000,
              metadata: {
                tenant_id: '00000000-0000-0000-0000-000000000001',
                subscription_id: '00000000-0000-0000-0000-000000000002'
              }
            },
            {
              id: 'in_wrong_tenant',
              customer: 'cus_123',
              subscription: 'sub_123',
              status: 'open',
              amount_due: 1500,
              amount_paid: 0,
              currency: 'usd',
              created: 1760000100,
              metadata: {
                tenant_id: '00000000-0000-0000-0000-000000000099',
                subscription_id: '00000000-0000-0000-0000-000000000002'
              }
            }
          ]
        };
      }
    })
  );

  const invoices = await gateway.listInvoicesForSync({
    tenantId: '00000000-0000-0000-0000-000000000001',
    subscriptionId: '00000000-0000-0000-0000-000000000002',
    providerCustomerId: 'cus_123',
    providerSubscriptionId: 'sub_123',
    correlationId: 'corr_invoice_sync_1',
    limit: 20
  });

  assert.equal(invoices.length, 1);
  assert.equal(invoices[0].providerInvoiceId, 'in_valid');
  assert.equal(invoices[0].tenantId, '00000000-0000-0000-0000-000000000001');
});
