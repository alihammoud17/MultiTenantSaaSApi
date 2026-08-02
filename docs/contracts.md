# Service Contracts

## Contract principles

Internal contracts are explicit, versioned, authenticated, traceable, and tenant-safe. Raw provider payloads do not become .NET domain contracts. External tenant and subscription identifiers must be verified against API-owned records before state changes are applied.

## Internal billing subscription events

### Endpoint

```http
POST /api/internal/billing/subscription-events
```

The .NET API consumes this contract and remains the internal subscription system of record. BillingService is the intended producer.

### Authentication

Requests use:

- `X-Billing-Timestamp`: Unix timestamp in seconds.
- `X-Billing-Signature`: lowercase hexadecimal HMAC-SHA256.

The signing input is:

```text
<timestamp>.<raw request body>
```

The key is `BillingIntegration:SharedSecret`. The API rejects absent or invalid signatures and timestamps outside `BillingIntegration:AllowedClockSkewMinutes`. Sign the exact bytes sent; JSON reserialization can change the signature.

### Current version and body

Current contract version: `2026-03-18`.

```json
{
  "contractVersion": "2026-03-18",
  "eventId": "evt_123",
  "eventType": "subscription.renewed",
  "provider": "stripe",
  "providerEventId": "stripe_evt_123",
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "subscriptionId": "00000000-0000-0000-0000-000000000002",
  "targetPlanId": "00000000-0000-0000-0000-000000000003",
  "occurredAtUtc": "2026-03-18T12:00:00Z",
  "effectiveAtUtc": "2026-03-18T12:00:00Z",
  "correlationId": "corr_123"
}
```

Required common fields are `contractVersion`, `eventId`, `eventType`, `provider`, `providerEventId`, `tenantId`, `subscriptionId`, `occurredAtUtc`, and `correlationId`. `targetPlanId` and `effectiveAtUtc` are event-dependent.

Supported lifecycle types include activation, renewal, plan change, cancellation, expiration, and payment failure. Exact accepted values and event-specific rules are guarded by the .NET integration and BillingService producer tests; a new event or field rule requires coordinated contract and test changes.

### Validation and idempotency

The consumer verifies that:

1. the contract version is supported;
2. required fields and timestamps are valid;
3. tenant and subscription IDs map to API-owned records;
4. the subscription belongs to the stated tenant;
5. a target plan exists when required; and
6. the event has not already been applied.

The event inbox stores external event identity for replay protection. Duplicate delivery must return a safe result without applying the lifecycle transition twice. `correlationId` supports cross-service traceability but is not an authorization credential.

### Conformance tests

- `.NET consumer`: `Tests/Integration/CrossServiceBillingContractTests.cs` and internal billing security/lifecycle suites.
- `BillingService producer`: `BillingService/tests/billingCallbackContract.test.ts` and replay fixtures.

These tests prove local payload, version, authentication, and replay conformance. They do not prove a deployed BillingService callback transport, because the default publisher is not wired to HTTP.

## Outbound tenant webhooks

### Envelope

Outbound events use the persisted event identity, event type, tenant identity, occurrence time, correlation identity, and JSON payload. Current contract version: `2026-04-13`.

### Delivery headers

- `X-Tenant-Webhook-Contract-Version`
- `X-Tenant-Webhook-Timestamp`
- `X-Tenant-Webhook-Delivery`
- `X-Tenant-Webhook-Idempotency-Key`
- `X-Tenant-Webhook-Signature`

The signature is an HMAC-SHA256 derived from the endpoint signing secret and the canonical timestamp, delivery identity, and payload used by the API signer. Consumers must verify against the raw body and reject stale or invalid requests.

### Delivery and replay behavior

Each endpoint delivery has a stable delivery ID and idempotency key. Non-success responses or transport failures are retried according to dispatcher policy until success or terminal exhaustion. Consumers should also deduplicate by the delivery/idempotency identity because successful HTTP acknowledgement cannot eliminate every network replay scenario.

Source-event and event identity uniqueness prevent duplicate materialization in the API. Endpoint and delivery access remain tenant-scoped. Signing secrets must never be logged or committed.

### Rotation limitation

Endpoint management can initiate rotation and issue pending signing material. The current documentation does not claim a complete automated active-secret cutover/finalization procedure; operators must not assume rotation is complete merely because a pending secret was issued.
