# BillingService

Minimal TypeScript Node.js companion service scaffold for billing, webhook ingestion, and future subscription workflow processing.

## Scope today

This service currently provides:
- a health endpoint
- a metrics endpoint
- structured JSON logging with correlation/trace ids
- a placeholder webhook endpoint
- a placeholder provider adapter abstraction
- a retry-safe subscription sync job that transforms normalized lifecycle events into the internal .NET callback contract
- a minimal folder layout for future Stripe/Paddle work
- shared internal contract types for future .NET callback integration

This service does **not** yet:
- call the .NET API
- contain provider SDK integrations
- store provider secrets
- verify/provider-map live webhook payloads yet
- persist webhook events outside the current in-memory retry/deduplication job

## Folder structure

```text
BillingService/
├── src/
│   ├── app.ts
│   ├── index.ts
│   ├── config/
│   ├── jobs/
│   ├── providers/
│   ├── routes/
│   ├── shared/
│   └── webhooks/handlers/
├── tests/
├── package.json
└── tsconfig.json
```

## Environment variables

All configuration is optional for the current placeholder service.

- `PORT` - HTTP port, defaults to `3001`
- `NODE_ENV` - environment name, defaults to `development`
- `BILLING_PROVIDER` - `placeholder`, `stripe`, or `paddle`; current scaffold always uses the placeholder adapter
- `WEBHOOK_SIGNING_SECRET` - reserved for future provider signature verification
- `DOTNET_CALLBACK_BASE_URL` - reserved for future authenticated callbacks into the .NET API
- `SERVICE_NAME` - optional service label for health/metrics payloads, defaults to `billing-service`

> Do not place real provider secrets in the repository.

## Run locally

From the repository root:

```bash
cd BillingService
npm run dev
```

The service starts on `http://localhost:3001` by default.

## Build and test

```bash
cd BillingService
npm run build
npm test
```

## Endpoints

### `GET /health`
Returns a structured JSON status payload with a simple self-check, correlation id, and embedded metrics summary.

### `GET /metrics`
Returns a lightweight in-memory JSON snapshot for active requests and request counts by route/status.

### `POST /webhooks/provider`
Accepts placeholder webhook requests and returns a `202 Accepted` response explaining that live processing is not implemented yet.

## Future extension points

- See `docs/technical-documentation.md` for a detailed class, method, property, and architecture reference.
- Replace the placeholder provider adapter with dedicated Stripe and Paddle adapters.
- Add signature verification and idempotent event storage.
- Add authenticated callback contracts toward the .NET API while keeping the .NET API as the system of record.
- Move placeholder job wiring to a real retry-safe queue/worker model when lifecycle processing begins.

## Observability notes

- All request and job logs are emitted as JSON lines to stdout/stderr.
- Send `x-correlation-id` to reuse a caller-provided correlation value; the service echoes it back on responses.
- The service also returns an `x-trace-id` header to make future tracing integrations easier without changing the request pipeline.
