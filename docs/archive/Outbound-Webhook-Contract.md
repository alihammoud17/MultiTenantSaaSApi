# Outbound Webhook Contract (Tenant Events)

## Status
Implemented foundation slice in the .NET API (contract version `2026-04-13`).

## Envelope

```json
{
  "contractVersion": "2026-04-13",
  "eventId": "owevt_...",
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "eventType": "tenant.subscription.updated",
  "correlationId": "corr-...",
  "occurredAtUtc": "2026-04-13T00:00:00Z",
  "data": {}
}
```

## Delivery headers

- `X-Tenant-Webhook-Contract-Version`
- `X-Tenant-Webhook-Timestamp` (ISO-8601 UTC)
- `X-Tenant-Webhook-Delivery` (stable GUID for one endpoint delivery)
- `X-Tenant-Webhook-Idempotency-Key` (stable per delivery)
- `X-Tenant-Webhook-Signature`

## Signature strategy

- Algorithm: `HMAC-SHA256`
- Signature payload format:

```text
{timestamp}\n{deliveryId}\n{rawJsonPayload}
```

- Header format: `sha256=<lowercase hex digest>`

Receivers should reject stale timestamps and verify signatures before parsing business data.

## Retry + delivery status

Persisted status states:

- `Pending`
- `RetryScheduled`
- `Succeeded`
- `Exhausted`

Behavior:

- non-2xx responses and transport failures schedule retry using exponential backoff
- maximum attempts are bounded; after final failure, delivery moves to `Exhausted`

## Idempotency and replay handling

Publisher-side safety:

- `SourceEventKey` deduplicates publish requests tied to upstream events (e.g., internal billing event ids)
- event records keep stable `eventId` and correlation metadata for traceability

Consumer guidance:

- persist and dedupe on `X-Tenant-Webhook-Idempotency-Key` (or `X-Tenant-Webhook-Delivery`)
- enforce timestamp tolerance to limit replay windows
- treat webhook bodies as untrusted input until signature validation succeeds
