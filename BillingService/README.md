# BillingService

Node.js / TypeScript billing companion service for the multi-tenant SaaS platform.

## Service status

- **Current repository status:** V2 is complete at the platform level, and **V3 is the next phase**.
- `BillingService` is part of that V3 direction, but it is still a scaffold rather than a live provider integration.
- The service currently helps define the provider-facing boundary and the internal event shape expected by the .NET API.
- Do **not** treat this service as production-ready billing infrastructure yet.

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
- a minimal reconciliation job skeleton that summarizes pending/dead-letter backlog state
- tests covering health/metrics endpoints plus workflow retry, dead-letter, persistence, and reconciliation behavior

## Not implemented yet

The following work is still upcoming V3 work and should not be documented elsewhere as already complete:

- live billing provider SDK integration
- real webhook signature verification
- tenant/subscription mapping from provider payloads
- authenticated HTTP callback delivery into the .NET API
- provider-side state reconciliation logic beyond summary/skeleton reporting
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
- `WORKFLOW_STATE_PATH` - durable queue state file location, defaults to `<repo>/BillingService/.billing-workflow-state.json`
- `WORKFLOW_MAX_ATTEMPTS` - max delivery attempts before dead-letter, defaults to `3`
- `WORKFLOW_INITIAL_BACKOFF_MS` - first retry delay in milliseconds, defaults to `1000`
- `WORKFLOW_MAX_BACKOFF_MS` - retry delay cap in milliseconds, defaults to `30000`
- `WORKFLOW_POLL_INTERVAL_MS` - background worker polling interval, defaults to `2000`
- `RECONCILIATION_INTERVAL_MS` - reconciliation summary interval, defaults to `300000`

> Do not place real provider secrets in the repository.

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
4. evolve reconciliation from summary skeleton to provider/internal state comparison workflows

These changes should keep provider-specific logic inside `BillingService` and preserve the .NET API as the system of record.

## Related docs

- `../README.md`
- `../docs/V3-Implementation-Backlog.md`
- `../docs/Internal-Billing-Contract.md`
- `../docs/technical-documentation.md`

## Manual confirmation still recommended

Before treating this README as final for V3 planning, manually confirm:

- which billing provider will be implemented first
- whether Node.js `>=22` is the intended long-term runtime requirement
- whether `npm install` should remain the documented setup step or be replaced by a lockfile-driven CI/local workflow
- whether any new runbook or ops documentation should be added alongside the first live billing integration slice
