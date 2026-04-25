# Outbound Webhooks (V3 Foundation)

_Last updated: April 25, 2026_

## Scope and status

This document tracks the currently implemented **outbound tenant webhook foundation** in the .NET API and its V3 follow-up scope.

Current status:

- first infrastructure slice is implemented in the .NET API
- delivery contracts are versioned and signed
- retry/delivery state is durable and observable
- tenant self-service endpoint management is **not** implemented yet

For the exact payload/signature contract, see `docs/Outbound-Webhook-Contract.md`.

## Ownership and architecture boundary

Outbound webhook publication in this iteration is owned by the .NET API because it publishes events derived from internal tenant/subscription platform state.

`BillingService` remains the provider-facing boundary for inbound provider webhooks and provider-specific workflows.

## Implemented foundation capabilities

- versioned envelope contract (`2026-04-13`) for stable downstream parsing
- authenticated delivery using HMAC-SHA256 (`X-Tenant-Webhook-Signature`)
- signature binding includes timestamp and delivery id to reduce replay risk
- persisted delivery records with retry scheduling and terminal status outcomes
- publish-time dedupe using source-event identity (`SourceEventKey`)
- stable per-delivery idempotency key header (`X-Tenant-Webhook-Idempotency-Key`)

## P1.4 deterministic verification harness foundation

The first reusable outbound verification harness slice is now in place in `.NET` unit tests (`Tests/UnitTests/OutboundWebhooks/*`).

Current harness capabilities:

- deterministic one-batch dispatch invocation for due deliveries (without external webhook dependencies)
- queue-backed HTTP outcome simulation for delivery attempts (`2xx`, `5xx`, and bounded terminal failure paths)
- retry timing simulation through explicit due-at control of persisted delivery rows
- captured outbound request inspection for key headers (delivery id, idempotency key, signature metadata)
- persisted delivery state inspection (`AttemptCount`, `Status`, `LastResponseStatusCode`, `LastError`, `DeliveredAtUtc`)

Initial scenarios covered by the harness:

1. failed first delivery (`500`) -> retry scheduled -> forced-due second attempt -> success transition
2. repeated transient failures until max-attempt exhaustion with deterministic terminal state assertions

This is intentionally a thin first slice. It establishes reusable harness primitives first, then broader matrix coverage (including additional retry-window and dedupe matrices) can be layered incrementally in follow-up P1 work.

## Security expectations

The current foundation is designed to satisfy baseline outbound security expectations:

- contracts are explicit and versioned
- deliveries are signed (consumers must verify signatures)
- retries are explicit and trackable
- duplicate publication risk is reduced through source-event dedupe
- secrets must come from environment/configuration, never hardcoded

## Operational expectations

For production-hardening follow-up, prioritize:

- delivery success/failure/retry metrics and alerts
- operator replay tooling for failed deliveries
- clear runbook steps for endpoint failures and signature mismatch investigations

## Follow-up backlog

Planned follow-up (not completed in this iteration):

1. tenant-facing endpoint registration, secret rotation, and disable/reenable lifecycle
2. broader event-type coverage beyond the initial foundation event set
3. replay controls and self-service diagnostics for tenant operators
4. deeper dashboards/alerts integrated with V3 observability rollout
