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

## Internal tenant operations

### Endpoints

```http
POST /api/internal/tenants/{tenantId}/suspend
POST /api/internal/tenants/{tenantId}/reactivate
```

Operator-only tenant status changes. The API is the producer's only counterpart; there is no user identity on these requests.

### Authentication

- `X-Operations-Timestamp`: ISO 8601 timestamp, e.g. `2026-10-13T14:00:00.0000000+00:00`.
- `X-Operations-Signature`: `sha256=` followed by the lowercase hexadecimal HMAC-SHA256 of `<timestamp>\n<raw request body>` (a newline between the two).

The key is `TenantOperations:SharedSecret`, deliberately separate from the billing secret. An unset secret rejects every request. Timestamps outside `TenantOperations:AllowedClockSkewMinutes` (default 5) are rejected. Sign the exact bytes sent.

### Body

```json
{ "reason": "Chargeback investigation #1234", "actor": "ops:alice" }
```

`reason` is required to suspend (at most 500 characters) and optional to reactivate. `actor` is optional (at most 100 characters). It is recorded in the audit entry but not authenticated: anyone holding the secret can claim any actor.

### Behavior

| Request | Allowed from | Effect |
| --- | --- | --- |
| suspend | `Active` | Status `Suspended`; reason stored; every refresh token of the tenant revoked; `TENANT_SUSPENDED` audit entry. |
| reactivate | `Suspended` | Status `Active`; reason cleared; `TENANT_REACTIVATED` audit entry. Refresh tokens revoked by the suspension stay revoked. |

Responses: `200 { tenantId, status }`, `400` invalid body or missing reason, `401` missing or invalid signature, `404` unknown tenant, `409` transition not allowed from the current status. A request whose signed timestamp is not later than the tenant's last status change also returns `409`. Status changes are ordered by signed timestamp, so a captured older request cannot undo a newer one.

Integration coverage: `Tests/Integration/TenantSuspensionTests.cs`.

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
