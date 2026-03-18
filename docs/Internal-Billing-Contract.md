# Internal Billing Contract

## Purpose

This document defines the minimal internal callback contract between the Node.js billing service and the .NET API.

The .NET API remains the system of record for tenant and subscription state. The billing service is only allowed to submit authenticated, normalized lifecycle events through the internal callback endpoint.

## Endpoint

- `POST /api/internal/billing/subscription-events`

## Authentication

Requests must include:

- `X-Billing-Timestamp`
- `X-Billing-Signature`

The signature is calculated as:

- `sha256=` + lowercase hex `HMACSHA256(sharedSecret, $"{timestamp}\n{rawJsonBody}")`

The .NET API rejects requests when:

- the shared secret is missing
- the timestamp is missing or outside the allowed clock skew
- the signature does not match the raw request body

## Contract version

- `contractVersion: "2026-03-18"`

This keeps the callback payload explicit and versionable.

## Request body

```json
{
  "contractVersion": "2026-03-18",
  "eventId": "evt_123",
  "eventType": "subscription.plan_changed",
  "provider": "stripe",
  "providerEventId": "stripe_evt_123",
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "subscriptionId": "00000000-0000-0000-0000-000000000002",
  "targetPlanId": "plan-pro",
  "occurredAtUtc": "2026-03-18T12:00:00Z",
  "correlationId": "corr_123"
}
```

## Required fields

- `contractVersion`
- `eventId`
- `eventType`
- `provider`
- `providerEventId`
- `tenantId`
- `subscriptionId`
- `occurredAtUtc`
- `correlationId`

`targetPlanId` is required only for `subscription.plan_changed`.

## Supported event types

- `subscription.activated`
- `subscription.renewed`
- `subscription.plan_changed`
- `subscription.canceled`
- `subscription.expired`
- `invoice.payment_failed`

## State mapping

The .NET API applies the following internal mapping:

- `subscription.activated` -> `Active`
- `subscription.renewed` -> `Active`
- `subscription.plan_changed` -> `Active` and updates `PlanId`
- `subscription.canceled` -> `Canceled`
- `subscription.expired` -> `Expired`
- `invoice.payment_failed` -> `Expired`

For `subscription.activated` and `subscription.renewed`, the period window is reset from `occurredAtUtc` to `occurredAtUtc + 1 month`.

## Tenant mapping validation

The .NET API validates that:

- `tenantId` is not empty
- `subscriptionId` is not empty
- the subscription exists
- the subscription belongs to the supplied tenant
- the tenant is active
- any supplied `targetPlanId` resolves to an active internal plan

The API never trusts provider identifiers alone for tenant resolution.

## Idempotency

Processed events are stored in `BillingEventInboxes` using a unique `eventId`.

If the same `eventId` is submitted more than once, the API returns a successful duplicate response and does not reapply the state transition.

## Assumptions

- The Node.js billing service already verified external provider webhook authenticity before calling the .NET API.
- The Node.js billing service sends normalized internal event types instead of raw provider payloads.
- The shared secret is managed through environment-specific configuration and rotated outside this code change.
