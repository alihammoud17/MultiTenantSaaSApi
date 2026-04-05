# BillingService

Node.js / TypeScript billing companion service for the multi-tenant SaaS platform.

## Service status

- **Current repository status:** V2 is complete at the platform level, and **V3 is the next phase**.
- `BillingService` is part of that V3 direction, but it is still a scaffold rather than a live provider integration.
- The service now includes a durable workflow and reconciliation iteration that is intended for operational hardening before live provider cutover.
- Do **not** treat this service as production-ready billing infrastructure yet; provider integration and callback delivery are still pending.

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
- a placeholder provider adapter boundary
- a webhook handler shell that routes provider webhook requests through the adapter interface
- normalized internal subscription event and callback payload types shared within the service
- a durable file-backed workflow queue abstraction for billing event processing
- initial workflow worker wiring with retry/backoff policy and dead-letter handling
- persistent dedup/replay protection keyed by normalized `eventId` across service restarts
- reconciliation job logic that compares provider subscription snapshots to internal .NET subscription snapshots, detects drift, and enqueues deterministic correction actions
- tests covering health/metrics endpoints plus workflow retry, dead-letter, persistence, duplicate-safe reconciliation reruns, and drift detection behavior

## Durable workflow iteration status

The durable workflow iteration is now implemented as a **scaffolded operational slice**:

- workflow events are persisted to a local JSON state file so queue/retry/dead-letter state survives restarts
- deduplication is persisted so repeated normalized `eventId` values can be ignored safely across process restarts
- retries use bounded exponential backoff with configurable caps
- dead-letter entries are retained for operator triage
- reconciliation comparison logic can identify drift between provider snapshots and internal subscription snapshots, then enqueue deterministic correction intents

This is intentionally still pre-live because the default app wiring does not yet connect to real provider webhooks, provider APIs, or authenticated .NET callback delivery.

Operational procedures for this iteration are documented in `../docs/Billing-Workflow-Runbook.md`.

## Not implemented yet

The following work is still upcoming V3 work and should not be documented elsewhere as already complete:

- live billing provider SDK integration
- real webhook signature verification
- tenant/subscription mapping from provider payloads
- authenticated HTTP callback delivery into the .NET API
- live provider/internal state source integrations (the comparison job is implemented, but default app wiring still uses placeholder sources until provider/.NET fetch clients are connected)
- tenant-facing billing self-service functionality

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

## Local development

From the repository root:

```bash
cd BillingService
npm install
npm run dev
```

The service starts on `http://localhost:3001` by default.

## Build and test

From the repository root:

```bash
cd BillingService
npm run build
npm test
```

`npm test` compiles TypeScript and runs the Node test runner against `dist/tests/*.test.js`.

## Current endpoints

### `GET /health`
Returns a structured JSON status payload with a simple self-check, correlation id, and embedded metrics summary.

### `GET /metrics`
Returns a lightweight in-memory JSON snapshot for active requests and request counts by route/status.

### `POST /webhooks/provider`
Accepts placeholder webhook requests and returns a `202 Accepted` response indicating that live provider processing is not implemented yet.

## Planned V3 evolution

The next useful implementation slices for this service are:

1. connect the service to the .NET internal billing callback endpoint using the documented HMAC contract
2. replace the placeholder provider adapter with a real provider implementation
3. connect the durable queue worker to authenticated .NET callback delivery and provider adapters
4. wire real provider and .NET subscription state readers into the reconciliation comparator workflow

These changes should keep provider-specific logic inside `BillingService` and preserve the .NET API as the system of record.

## Related docs

- `../README.md`
- `../docs/V3-Implementation-Backlog.md`
- `../docs/Internal-Billing-Contract.md`
- `../docs/Billing-Workflow-Runbook.md`
- `../docs/technical-documentation.md`

## Manual confirmation still recommended

Before treating this README as final for V3 planning, manually confirm:

- which billing provider will be implemented first
- whether Node.js `>=22` is the intended long-term runtime requirement
- whether `npm install` should remain the documented setup step or be replaced by a lockfile-driven CI/local workflow
- whether any new runbook or ops documentation should be added alongside the first live billing integration slice
