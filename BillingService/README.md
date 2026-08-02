# BillingService

Node.js/TypeScript companion service for provider-facing billing concerns. The ASP.NET Core API remains the system of record for tenants, authorization, plans, entitlements, and internal subscription state.

## Current status

BillingService is a **pre-live, locally validated workflow foundation**, not a complete billing control plane.

Implemented:

- `GET /health` and `GET /metrics` JSON endpoints with correlation-aware diagnostics.
- `POST /webhooks/provider` ingress and normalized internal subscription-event types.
- File-backed queueing, event-id deduplication, exponential retry, dead-letter state, and restart recovery.
- Reconciliation comparison and deterministic correction-intent logic.
- Stripe HTTP gateway methods for tenant-tagged checkout sessions, billing-portal sessions, and tenant-validated invoice listing.
- Contract, replay, invalid-signature, workflow recovery, reconciliation, observability, and Stripe gateway tests.

Not wired in the default runtime:

- Real provider webhook verification. `placeholder`, `stripe`, and `paddle` currently select the placeholder webhook adapter.
- Authenticated HTTP callback delivery to the .NET API; the default publisher logs safely instead.
- Live provider and API reconciliation readers; scheduled runtime readers currently return empty lists.
- Stripe gateway selection from webhook or public checkout/portal routes.

## Boundaries

BillingService owns provider adapters, webhook verification and normalization, provider workflow durability, and reconciliation. It does not authorize tenant access or independently own internal subscription state. Every external tenant/subscription identity must be verified through API-owned mappings before it can affect authoritative state.

See [platform architecture](../docs/architecture.md) and [service contracts](../docs/contracts.md).

## Requirements

- Node.js 22 or later
- npm

## Install, run, and validate

```bash
cd BillingService
npm ci
npm run dev
```

Full service checks:

```bash
npm ci
npm run build
npm run typecheck
npm test
```

From the repository root, `scripts/dev.sh run`, `scripts/dev.sh smoke`, and `scripts/dev.sh test` provide the standardized cross-service path. Smoke checks placeholder acceptance only.

## Environment variables

| Variable | Default / status | Purpose |
| --- | --- | --- |
| `PORT` | `3001` | HTTP port. |
| `NODE_ENV` | `development` | Environment label. |
| `SERVICE_NAME` | `billing-service` | Service label used in diagnostics. |
| `BILLING_PROVIDER` | `placeholder` | Accepts `placeholder`, `stripe`, or `paddle`; all currently use placeholder webhook handling. |
| `WEBHOOK_SIGNING_SECRET` | unset | Intended provider signature secret; live verification is not wired. |
| `DOTNET_CALLBACK_BASE_URL` | unset | Intended callback destination; runtime dispatch is not wired. |
| `STRIPE_API_KEY` | unset | Required when constructing the Stripe gateway; sensitive. |
| `STRIPE_API_BASE_URL` | `https://api.stripe.com` | Stripe gateway base URL override. |
| `WORKFLOW_STATE_PATH` | `.billing-workflow-state.json` | Queue, retry, deduplication, and dead-letter JSON state path. |
| `WORKFLOW_MAX_ATTEMPTS` | `3` | Attempts before dead-letter. |
| `WORKFLOW_INITIAL_BACKOFF_MS` | `1000` | Initial retry delay. |
| `WORKFLOW_MAX_BACKOFF_MS` | `30000` | Backoff cap. |
| `WORKFLOW_POLL_INTERVAL_MS` | `2000` | Worker polling interval. |
| `RECONCILIATION_INTERVAL_MS` | `300000` | Reconciliation schedule interval. |

Never commit provider keys, webhook secrets, callback secrets, or populated env files. Parsing a variable does not mean the associated runtime path is connected.

## Endpoints

### `GET /health`

Returns structured service status, correlation context, and safe configuration/metrics information. It does not verify live provider connectivity, callback delivery, or reconciliation sources.

### `GET /metrics`

Returns an in-memory JSON request snapshot. It is not a production metrics exporter.

### `POST /webhooks/provider`

Accepts the configured adapter’s webhook input. In the current application factory, every provider setting resolves to the placeholder adapter; this endpoint therefore does not provide live provider signature verification.

## Workflow operations

The JSON state file must be writable and should be persisted across controlled restarts. It is not safe distributed coordination for multiple uncontrolled service instances.

For a failed item:

1. Locate event and correlation IDs in structured logs and workflow state.
2. Correct the transport, mapping, configuration, or code cause.
3. Confirm whether the event is processed or dead-lettered before replay.
4. Replay a small, controlled set and verify event-id deduplication.

Do not log secrets or full sensitive provider payloads. See the complete [operations runbook](../docs/operations.md#billing-workflow-recovery).

## Docker

From the repository root:

```bash
docker build -f deploy/billing.Dockerfile -t multitenant-saas-billing .
```

The Compose profile persists `WORKFLOW_STATE_PATH` under `/var/lib/billing/state` and loads runtime values from `/etc/multitenant-saas-api/billing.env`. See [Docker Compose operations](../docs/operations.md#docker-compose-local-vm-profile).

## Related documentation

- [Repository README](../README.md)
- [Architecture](../docs/architecture.md)
- [Operations](../docs/operations.md)
- [Contracts](../docs/contracts.md)
- [Decisions and open items](../docs/decisions.md)
- [Historical documentation](../docs/archive/README.md)
