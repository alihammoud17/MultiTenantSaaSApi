# Billing Hosting Evaluation (Current-State, Pre-Decision)

Date: April 29, 2026  
Scope: Repository-grounded evaluation of whether `BillingService` should remain separate or be collapsed into the .NET host.  
Decision status: **No decision proposed in this document**.

## 1) Current-state architecture snapshot

## Implemented runtime shape today

The repository currently runs as a two-service local platform:

1. **ASP.NET Core API (.NET)** as the system of record.
2. **Node.js BillingService** as the provider-facing billing companion service scaffold.

This split is explicitly documented in root and BillingService docs and reflected in local orchestration scripts that run both services side-by-side.

## Current internal billing callback flow (.NET side)

As implemented today:

1. Billing callback endpoint is `POST /api/internal/billing/subscription-events`.
2. Controller reads raw body, validates `X-Billing-Timestamp` + `X-Billing-Signature`, rejects invalid signatures with `401`.
3. Payload deserializes to internal callback DTO; malformed payload is rejected with `400`.
4. `BillingCallbackProcessor` enforces contract version, required fields, tenant/subscription mapping, and target plan validity.
5. Duplicate `eventId` is idempotent via `BillingEventInboxes` lookup.
6. Supported event types are applied to internal subscription lifecycle state.
7. Resulting event is persisted to `BillingEventInboxes` and a .NET outbound tenant webhook publication is triggered.

This means the current source-of-truth lifecycle mutation happens in .NET after signed internal callback validation, not in BillingService.

## Current BillingService runtime flow

As implemented today in app wiring:

1. BillingService exposes `/health`, `/metrics`, and `POST /webhooks/provider`.
2. `POST /webhooks/provider` delegates to `SubscriptionWebhookHandler`.
3. Default adapter is currently placeholder behavior that does not accept live webhook processing.
4. If accepted (in tests/fixtures or non-placeholder adapter scenarios), events are normalized and enqueued through durable workflow queueing.
5. `SubscriptionSyncJob` maps normalized events to internal callback payload shape (`contractVersion: 2026-03-18`) and routes through a callback publisher abstraction.
6. Default publisher is currently no-op (logs only), while tests inject publishers to validate retry/dead-letter/replay semantics.
7. Reconciliation job exists and enqueues deterministic correction intents based on provider-vs-internal drift classification, using same workflow queue/retry model.

So, callback contract generation and durable workflow logic are implemented, while fully wired live provider verification + authenticated .NET callback dispatch remain explicitly pre-live.

## 2) Current split responsibilities

## What the .NET API owns today (implemented)

- Tenant identity/authentication/authorization system-of-record concerns and tenant-scoped business state.
- Internal billing callback ingestion endpoint.
- Internal callback signature verification (timestamp + HMAC contract).
- Contract/version/required-field validation and tenant/subscription mapping enforcement.
- Subscription lifecycle state mutation and plan transition application.
- Callback idempotency persistence in `BillingEventInboxes`.
- Emission of outbound tenant webhook events after internal state mutation.

## What BillingService owns today (implemented)

- Companion service runtime, health/metrics endpoints, request correlation logging.
- Provider adapter boundary and webhook handling entry point.
- Normalization boundary from provider-shaped input to internal subscription-event model.
- Durable file-backed workflow queue with retry/backoff/dead-letter semantics.
- Persistent replay dedup by `eventId` across restarts.
- Reconciliation drift detection + correction-intent enqueue model.
- Producer-side callback contract payload shaping tests and replay/observability quality-gate tests.

## What is design-only / post-deployment (not implemented end-to-end)

- Live provider signature authenticity verification in active runtime paths.
- Fully wired authenticated BillingService -> .NET callback dispatch in live provider flow.
- Deployment-proven telemetry/exporters/alerts/SLO runbooks.

This distinction is already explicitly called out in current repository docs and should remain separated from implemented claims.

## 3) Current local workflow impact

The split is currently embedded in local deterministic workflow:

- `scripts/dev.sh` wrappers and `scripts/local/*` scripts bootstrap, run, smoke, and test **both services**.
- `bootstrap` installs/builds .NET and BillingService dependencies.
- `run` starts both services and captures per-service logs.
- `smoke` checks .NET health + BillingService health/metrics and placeholder webhook acceptance.
- `test` runs full .NET command sequence and BillingService install/build/test sequence.

Implication: collapsing hosting would change script semantics, smoke criteria, and failure-triage ordering currently tuned around dual-process operation.

## 4) Current testing/contract impact

Current test and contract coverage is explicitly split-aware:

- .NET integration tests validate internal callback acceptance/rejection/idempotency and tenant-isolation enforcement.
- .NET cross-service contract tests validate callback signature/version/field/mapping behavior as consumer-side guarantees.
- BillingService producer contract tests validate payload shape/version and provider-event fallback behavior.
- BillingService workflow tests validate durable queue retry/dead-letter/persistence recovery semantics.
- BillingService fixture replay tests validate duplicate/out-of-order/stale/invalid-signature scenarios.
- BillingService observability quality gates validate correlation continuity and diagnostics sanitization for webhook/workflow failure paths.

Implication: host-collapse evaluation must account for preserving these guarantees (or replacing them with equivalent tests) because V4 hardening work already encodes the current split.

## 5) Current observability/diagnostics impact

Current observability is also split-aware:

- Each service exposes independent `/health` + `/metrics` contracts used by smoke checks.
- Local logs are captured per service (`.local-api.log`, `.local-billing.log`).
- Correlation continuity is tested across webhook/callback paths in both codebases.
- BillingService diagnostics include workflow lifecycle signals (queued/retry/dead-letter/reconciliation summaries).
- .NET diagnostics include callback processing outcomes, duplicate handling, and outbound webhook dispatch transitions.

Implication: any future hosting model change must preserve diagnosability parity (not just feature parity) for V4 quality-gate intent.

## 6) Open questions to answer before deciding

No architecture decision is proposed yet because repository evidence is still incomplete for a safe conclusion. The following questions should be answered first:

1. **Provider realism gap:** What exact remaining implementation work is required to replace placeholder webhook behavior with real verified provider ingestion in current split?
2. **Callback delivery gap:** What concrete code path will perform authenticated BillingService -> .NET dispatch in runtime (beyond no-op publisher/test-injected publishers), and how will it be validated locally?
3. **Test migration cost:** If hosting collapsed, which current BillingService workflow/contract/replay tests can be preserved unchanged, which must be ported, and what confidence loss risk exists during transition?
4. **Orchestration impact:** How would `scripts/dev.sh` + `scripts/local/*` be reworked while preserving deterministic two-service validation guarantees currently used in V4?
5. **Operational boundaries:** Would reconciliation, retry, dead-letter state, and replay-dedup remain operationally isolated with equivalent transparency if moved in-process to .NET?
6. **Contract governance:** How would explicit producer/consumer contract conformance checks remain version-safe if both producer and consumer lived in one host?
7. **Failure-domain behavior:** What is the measured impact on failure isolation and blast radius if webhook normalization, retry workflows, and .NET system-of-record processing share one process/runtime?
8. **Documentation consistency plan:** Which existing docs (README, BillingService/README, V4 backlog, runbooks, internal billing contract docs) would need coordinated updates to avoid overstating implemented behavior?

Until these are answered with repository-backed evidence (code/test/runbook updates), deferring a host-model decision is the safer current-state position.
