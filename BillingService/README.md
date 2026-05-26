# BillingService

Node.js / TypeScript billing companion service for the multi-tenant SaaS platform.

## Service status

- **Current repository status:** V1, V2, and V3 are complete at the platform level.
- **V4 pre-deployment P0 slices are implemented through April 19, 2026**, including cross-service contract conformance and replay/idempotency fixture validation.
- `BillingService` remains a pre-live provider-facing companion service with explicit boundaries between local-demo capability and post-deployment work.

## Responsibility boundary

`BillingService` is intended to own:

- billing provider integration
- provider webhook ingestion and verification
- provider-to-internal billing event normalization
- retry-safe billing workflow processing
- reconciliation with provider state
- secure communication with the .NET API

The .NET API remains the system of record for tenant identity, authorization, and internal subscription state.

## Implemented today

The current codebase provides a pre-live scaffold with the following implemented pieces:

- `GET /health` endpoint with a structured status payload
- `GET /metrics` endpoint with lightweight in-memory request metrics
- structured JSON logging with correlation and trace identifiers
- request context propagation for correlation headers
- a placeholder provider webhook adapter boundary
- a Stripe provider API gateway slice for tenant checkout session creation, billing-portal session creation, and invoice listing for sync workflows
- a webhook handler shell that routes provider webhook requests through the adapter interface
- normalized internal subscription event and callback payload types shared within the service
- a durable file-backed workflow queue abstraction for billing event processing
- initial workflow worker wiring with retry/backoff policy and dead-letter handling
- persistent dedup/replay protection keyed by normalized `eventId` across service restarts
- reconciliation job logic that compares provider subscription snapshots to internal .NET subscription snapshots, detects drift, and enqueues deterministic correction actions
- tests covering health/metrics endpoints, webhook handler accepted/duplicate behavior, workflow retry/dead-letter/persistence recovery, duplicate-safe reconciliation reruns, and drift classification behavior
- provider gateway tests covering tenant-safe checkout/portal session metadata, invoice tenant-mapping filtering, and clean checkout/portal session error surfacing

## Durable workflow iteration status

The durable workflow iteration is now implemented as a **scaffolded operational slice**:

- workflow events are persisted to a local JSON state file so queue/retry/dead-letter state survives restarts
- deduplication is persisted so repeated normalized `eventId` values can be ignored safely across process restarts
- retries use bounded exponential backoff with configurable caps
- dead-letter entries are retained for operator triage
- reconciliation comparison logic can identify drift between provider snapshots and internal subscription snapshots, then enqueue deterministic correction intents

This is intentionally still pre-live because the default app wiring does not yet connect to real provider webhooks, provider APIs, or authenticated .NET callback delivery.

Operational procedures for this iteration are documented in `../docs/Billing-Workflow-Runbook.md`.

## Design-only / post-V3 roadmap (not implemented here)

V3 is closed. The items below are **not** open V3 scope; they are future roadmap/design candidates unless and until implemented in code:

- additional provider expansion beyond the currently implemented provider-facing slice
- deeper tenant-facing billing management features owned by the .NET API surface
- further reconciliation automation and operator tooling beyond the current baseline
- exporter/dashboard/alert maturation described in `../docs/V3-Observability-and-Operations-Design.md`

## Current folder structure

```text
BillingService/
├── src/
│   ├── app.ts
│   ├── index.ts
│   ├── config/
│   ├── jobs/
│   ├── observability/
│   ├── providers/
│   ├── routes/
│   ├── shared/
│   └── webhooks/handlers/
├── tests/
├── package.json
└── tsconfig.json
```

## Environment variables

The current scaffold can run with defaults, but these variables define the intended integration surface:

- `PORT` - HTTP port, defaults to `3001`
- `NODE_ENV` - environment name, defaults to `development`
- `BILLING_PROVIDER` - `placeholder`, `stripe`, or `paddle`; current code still uses the placeholder provider path
- `WEBHOOK_SIGNING_SECRET` - reserved for future provider signature verification
- `DOTNET_CALLBACK_BASE_URL` - reserved for future authenticated callbacks into the .NET API
- `STRIPE_API_KEY` - required when Stripe tenant billing provider calls are enabled
- `STRIPE_API_BASE_URL` - optional Stripe API base URL override, defaults to `https://api.stripe.com`
- `SERVICE_NAME` - optional service label for health/metrics payloads, defaults to `billing-service`
- `WORKFLOW_STATE_PATH` - durable queue/retry/dead-letter state file path, defaults to `<repo>/BillingService/.billing-workflow-state.json`
- `WORKFLOW_MAX_ATTEMPTS` - max delivery attempts before dead-letter, defaults to `3`
- `WORKFLOW_INITIAL_BACKOFF_MS` - first retry delay in milliseconds, defaults to `1000`
- `WORKFLOW_MAX_BACKOFF_MS` - retry delay cap in milliseconds, defaults to `30000`
- `WORKFLOW_POLL_INTERVAL_MS` - background worker polling interval, defaults to `2000`
- `RECONCILIATION_INTERVAL_MS` - reconciliation polling/snapshot interval, defaults to `300000`

> Do not place real provider secrets in the repository.

### Durable workflow quick-start env template

For local durability/reconciliation checks, you can run with explicit values:

```bash
export WORKFLOW_STATE_PATH="$(pwd)/.billing-workflow-state.json"
export WORKFLOW_MAX_ATTEMPTS=3
export WORKFLOW_INITIAL_BACKOFF_MS=1000
export WORKFLOW_MAX_BACKOFF_MS=30000
export WORKFLOW_POLL_INTERVAL_MS=2000
export RECONCILIATION_INTERVAL_MS=300000
```

These defaults match the current runbook guidance in `../docs/Billing-Workflow-Runbook.md`.

## Docker (BillingService only)

From the repository root, build the BillingService image with:

```bash
docker build -f deploy/billing.Dockerfile -t multitenant-billing:local .
```

The image build is multi-stage and compiles TypeScript in a build stage, then installs production-only dependencies in the runtime stage before starting with `npm run start`.

## Local development

From the repository root:

```bash
cd BillingService
npm install
npm run typecheck
npm run build
npm run dev
```

The service starts on `http://localhost:3001` by default.

For deterministic platform orchestration from the repository root, use the standardized wrapper commands:

```bash
scripts/dev.sh bootstrap
scripts/dev.sh reset
scripts/dev.sh seed
scripts/dev.sh run
scripts/dev.sh smoke
scripts/dev.sh test
```

`scripts/dev.sh` delegates to `scripts/local/*.sh` so the underlying script behavior remains explicit and directly usable.
`run.sh` starts both services and keeps them running until interrupted; `smoke.sh` should be run from a second shell while `run.sh` is active.

### Orchestration profile quick reference

Local bootstrap path (code-ready setup):

```bash
scripts/dev.sh bootstrap
# optional clean-state reset:
scripts/dev.sh reset
scripts/dev.sh seed
```

Local smoke path (code-ready runtime check):

```bash
# shell A
scripts/dev.sh run

# shell B
scripts/dev.sh smoke
```

Local test path (cross-service validation):

```bash
scripts/dev.sh test
```

Smoke validates service health + metrics endpoint availability and placeholder webhook acceptance only; it does **not** prove live provider webhook verification or authenticated callback delivery into the .NET API.

### Recommended execution order

Use this command order for deterministic local validation:

1. `scripts/dev.sh bootstrap`
2. `scripts/dev.sh seed`
3. `scripts/dev.sh run` (shell A)
4. `scripts/dev.sh smoke` (shell B)
5. `scripts/dev.sh test` (shell B, after smoke passes)

If a failure occurs, resolve it before moving to the next step. The detailed triage matrix (dependencies, migrations/tooling, startup, ports/config, smoke, tests) is in `../docs/Local-Orchestration-Profile.md`.

For script flags/overrides and troubleshooting, see `../docs/Local-Orchestration-Profile.md`.


## V4 pre-deployment capability map (BillingService scope, April 19, 2026)

| Capability | Status | Demoable locally today | Post-deployment remaining |
| --- | --- | --- | --- |
| Service runtime health/metrics/logging | Implemented | `GET /health`, `GET /metrics`, and structured correlation-aware logs. | Production exporter wiring, alerting thresholds, and on-call runbooks validated in deployed environments. |
| Provider webhook intake boundary | Implemented (placeholder path) | `POST /webhooks/provider` acceptance via local smoke path. | Real provider signature verification and provider-specific ingestion hardening in active runtime. |
| Durable queue + retry + dead-letter + replay dedup | Implemented | File-backed workflow state survives restarts; duplicate event handling is replay-safe by `eventId`. | Operations dashboards and replay tooling validated under production traffic. |
| Cross-service callback payload conformance | Implemented | `tests/billingCallbackContract.test.ts` verifies emitted payload shape/version and `providerEventId` fallback behavior. | Multi-version rollout compatibility checks across deployed service versions. |
| Fixture-driven replay behavior coverage | Implemented | `tests/billingEventFixtureReplay.test.ts` validates duplicate/out-of-order/stale/invalid-signature scenarios deterministically. | Incident-derived fixture expansion from live provider failure/latency patterns. |
| Authenticated callback delivery to .NET runtime | Partial / pre-live | Contract payload generation is demoable through tests and local harness behavior. | End-to-end authenticated callback dispatch from live provider events in deployed environments. |

### BillingService local demo scope today

- deterministic local startup as part of repository orchestration scripts
- placeholder webhook acceptance and API gateway slices (checkout, portal, invoice list)
- durable/replay-safe workflow primitives with local file-backed state
- contract conformance and replay safety shown via automated test suites

### BillingService post-deployment scope

- verified live webhook authenticity with provider signing secrets
- fully wired authenticated callback dispatch into the .NET API in runtime paths
- production telemetry/alerting/SLO operations maturity beyond local metrics/logging

## Code-ready local validation vs production readiness

**Code-ready local validation (implemented):**

- deterministic bootstrap/run/smoke workflow via `scripts/local/`
- local BillingService runtime validation via `/health`, `/metrics`, and placeholder webhook acceptance smoke checks (including explicit JSON checks for both health and metrics)
- repeatable local log capture through `.local-api.log` and `.local-billing.log` defaults

**Production readiness (not claimed yet):**

- live provider webhook signature verification in active runtime flow
- authenticated BillingService-to-.NET callback delivery wired and validated end-to-end
- production-proven telemetry/exporter/alert runbooks and on-call grade operations automation

## Build and test

From the repository root:

```bash
cd BillingService
npm run build
npm test
```

`npm test` runs the Node test runner directly against `tests/*.test.ts` using Node's TypeScript transform mode (`--experimental-transform-types`) for deterministic local execution without a pre-build step.

The test suite includes callback-producer contract coverage (`tests/billingCallbackContract.test.ts`) that verifies:

- emitted callback payload compatibility with `../docs/Internal-Billing-Contract.md`
- fixed contract version emission (`2026-03-18`)
- `providerEventId` fallback to `eventId` when provider payloads do not include a provider event id

The test suite also includes a fixture-driven replay slice (`tests/billingEventFixtureReplay.test.ts`) with reusable scenarios in `tests/fixtures/billingEventFixturePack.ts` that cover:

- duplicate delivery replay safety (`eventId` dedup persistence)
- out-of-order delivery sequencing behavior
- stale timestamp event handling behavior in the current workflow
- invalid signature rejection before workflow enqueue

Observability quality-gate coverage now also includes `tests/observabilityQualityGates.test.ts`, which enforces:

- correlation-id continuity for representative webhook request handling
- required safe structured fields on request lifecycle logs (`http.request.started` / `http.request.completed`)
- required safe structured fields on workflow dead-letter diagnostics (`eventId`, `correlationId`, `tenantId`, `status`, `attempts`, `message`, `timestamp`)
- negative-path diagnostics + sanitization guarantees for transient retry scheduling, terminal dead-letter/retry exhaustion, and webhook rejection reasons (preserved failure context with sensitive token/secret/header value absence)

P1.5 local observability quality-gate status (April 26, 2026):

- **Locally enforced now**
  - `/health` and `/metrics` JSON availability is guarded by smoke/test coverage.
  - representative request/workflow observability logs require correlation-safe structured fields.
  - webhook rejection and retry/dead-letter diagnostics are required to remain actionable while sanitizing sensitive values.
- **Partially implemented surfaces**
  - log-schema and forbidden-field coverage is representative for high-value paths; it is not yet a repository-wide schema gate over every BillingService log event.
  - trace continuity remains local/correlation-derived and is not yet full distributed `traceparent` propagation.
- **Future production telemetry (out of scope in P1.5)**
  - exporter wiring, environment telemetry backends, dashboard/alert thresholds, and on-call calibration remain post-P1.5 work.

## Current endpoints

### `GET /health`
Returns a structured JSON status payload with a simple self-check, correlation id, and embedded metrics summary.

### `GET /metrics`
Returns a lightweight in-memory JSON snapshot for active requests and request counts by route/status.

### `POST /webhooks/provider`
Accepts placeholder webhook requests and returns a `202 Accepted` response indicating that live provider processing is not implemented yet.

## Post-V3 roadmap candidates

Potential post-V3 implementation slices for this service are:

1. connect the service to the .NET internal billing callback endpoint using the documented HMAC contract
2. replace the placeholder webhook adapter with a real webhook verification implementation
3. connect the durable queue worker to authenticated .NET callback delivery and provider adapters
4. wire real provider and .NET subscription state readers into the reconciliation comparator workflow

These changes should keep provider-specific logic inside `BillingService` and preserve the .NET API as the system of record.

## Related docs

- `../README.md`
- `../docs/V4-Implementation-Backlog.md`
- `../docs/Internal-Billing-Contract.md`
- `../docs/Billing-Workflow-Runbook.md`
- `../docs/V3-Observability-and-Operations-Design.md`
- `../docs/technical-documentation.md`

## Manual confirmation still recommended

For post-V3 roadmap execution, manually confirm:

- which billing provider will be implemented first
- whether Node.js `>=22` is the intended long-term runtime requirement
- whether `npm install` should remain the documented setup step or be replaced by a lockfile-driven CI/local workflow
- whether any new runbook or ops documentation should be added alongside the first live billing integration slice
