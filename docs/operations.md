# Operations and Local Development

## Prerequisites

- .NET 10 SDK
- Node.js 22 or later and npm
- PostgreSQL 16 or later
- Redis 7 or later
- EF Core CLI at `/tmp/dotnet-tools/dotnet-ef` for repository scripts, or a working `dotnet ef` command for manual operations

## Standard local workflow

From the repository root:

```bash
scripts/dev.sh bootstrap
scripts/dev.sh seed
scripts/dev.sh run
```

In a second shell:

```bash
scripts/dev.sh smoke
scripts/dev.sh test
```

Use `scripts/dev.sh reset` before `seed` for a clean database and BillingService workflow state. `scripts/dev.sh help` lists supported commands. The wrapper dispatches to transparent scripts under `scripts/local/`.

### Command behavior

- `bootstrap`: restores/builds .NET, applies migrations when the configured EF tool is available, and installs BillingService dependencies with `npm ci`.
- `reset`: removes local service logs and BillingService state, drops the local database, and reapplies migrations.
- `seed`: reapplies migrations and reports the current manual boundary for scenario/demo data.
- `run`: starts both services in the foreground and captures local logs.
- `smoke`: checks API and BillingService health/metrics and placeholder webhook acceptance.
- `test`: runs the required .NET and BillingService validation suites.

Smoke success does not prove provider signature verification, authenticated callback delivery, production telemetry, or external dependency readiness.

## Configuration reference

### ASP.NET Core API

Configuration can use user secrets for development or double-underscore environment variables:

| Configuration key | Environment variable | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | PostgreSQL connection string; required. |
| `Redis:ConnectionString` | `Redis__ConnectionString` | Redis rate-limit connection; required at startup. |
| `Jwt:Secret` | `Jwt__Secret` | JWT signing secret; required and sensitive. |
| `Jwt:Issuer` | `Jwt__Issuer` | Valid token issuer; default documentation value is `MultiTenantSaasApi`. |
| `Jwt:Audience` | `Jwt__Audience` | Valid token audience; default documentation value is `MultiTenantSaasApi`. |
| `Jwt:ExpirationMinutes` | `Jwt__ExpirationMinutes` | Access-token lifetime; documented default is `60`. |
| `BillingIntegration:SharedSecret` | `BillingIntegration__SharedSecret` | Internal callback HMAC key; required and sensitive. |
| `BillingIntegration:AllowedClockSkewMinutes` | `BillingIntegration__AllowedClockSkewMinutes` | Callback timestamp tolerance; default is `5`. |

Example development setup from `Presentation/`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=saasapi;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "Jwt:Secret" "YOUR_STRONG_SECRET"
dotnet user-secrets set "Jwt:Issuer" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:Audience" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:ExpirationMinutes" "60"
dotnet user-secrets set "BillingIntegration:SharedSecret" "YOUR_INTERNAL_SHARED_SECRET"
dotnet user-secrets set "BillingIntegration:AllowedClockSkewMinutes" "5"
```

### BillingService

| Variable | Default / status | Purpose |
| --- | --- | --- |
| `PORT` | `3001` | HTTP listen port. |
| `NODE_ENV` | `development` | Runtime environment label. |
| `SERVICE_NAME` | `billing-service` | Health, metrics, and log service name. |
| `BILLING_PROVIDER` | `placeholder` | Accepts `placeholder`, `stripe`, or `paddle`; all currently select the placeholder webhook adapter. |
| `WEBHOOK_SIGNING_SECRET` | unset | Intended provider webhook signature secret; live verification is not wired. |
| `DOTNET_CALLBACK_BASE_URL` | unset | Intended API callback base URL; default callback delivery is not wired. |
| `STRIPE_API_KEY` | unset | Required by the Stripe gateway when explicitly constructed; sensitive. |
| `STRIPE_API_BASE_URL` | `https://api.stripe.com` | Stripe API override for the gateway slice. |
| `WORKFLOW_STATE_PATH` | `.billing-workflow-state.json` in the working directory | Queue, retry, dedupe, and dead-letter JSON state. |
| `WORKFLOW_MAX_ATTEMPTS` | `3` | Attempts before dead-letter. |
| `WORKFLOW_INITIAL_BACKOFF_MS` | `1000` | Initial retry delay. |
| `WORKFLOW_MAX_BACKOFF_MS` | `30000` | Retry-delay cap. |
| `WORKFLOW_POLL_INTERVAL_MS` | `2000` | Worker polling interval. |
| `RECONCILIATION_INTERVAL_MS` | `300000` | Scheduled reconciliation interval. |

Do not infer runtime capability from a parsed variable: provider selection, callback delivery, and reconciliation readers remain scaffolded.

## Database migrations

From the repository root:

```bash
/tmp/dotnet-tools/dotnet-ef database update --project Infrastructure --startup-project Presentation
```

If the tool is installed on `PATH`, the equivalent is:

```bash
dotnet ef database update --project Infrastructure --startup-project Presentation
```

Run migrations before local startup after pulling schema changes. Do not edit generated migrations to solve documentation or startup problems.

## Direct service startup

API:

```bash
dotnet run --project Presentation
```

BillingService:

```bash
cd BillingService
npm ci
npm run dev
```

Swagger UI is available only in Development and Testing.

## Validation and tests

Required .NET checks:

```bash
dotnet --info
/tmp/dotnet-tools/dotnet-ef --version
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
```

BillingService checks:

```bash
cd BillingService
npm ci
npm run build
npm run typecheck
npm test
```

The repository wrapper `scripts/dev.sh test` performs the standardized cross-service sequence.

## Health, metrics, and logs

### API

- `GET /health` returns JSON with correlation ID, duration, self status, and PostgreSQL status.
- Redis is not included in the health report.
- `GET /metrics` returns an in-memory JSON snapshot.

### BillingService

- `GET /health` returns service/configuration-safe status and request context.
- `GET /metrics` returns in-memory route/status counters.
- Health does not verify live provider access, API callback delivery, reconciliation readers, or state-file integrity under concurrent instances.

Both services use structured correlation-aware logging. Local run scripts write `.local-api.log` and `.local-billing.log`. Do not log JWTs, passwords, provider/API keys, HMAC secrets, signing secrets, or full sensitive payloads.

The JSON metrics endpoints are local diagnostics, not OpenTelemetry or Prometheus exporters. Production sinks, dashboards, alerts, and SLOs remain open decisions.

## Billing workflow recovery

The BillingService state file contains queue, retry, deduplication, and dead-letter records. Keep it on a writable persistent path and do not allow multiple uncontrolled writers.

For dead-letter triage:

1. Locate the event and correlation IDs in structured logs and workflow state.
2. Classify the failure as transient transport, permanent validation/mapping, configuration, or code failure.
3. Correct the cause before replay.
4. Confirm whether the event is already processed or dead-lettered.
5. Replay only the affected event in a controlled environment.
6. Verify event-id deduplication prevents duplicate side effects.

The default publisher only logs callback payloads, so successful local queue processing does not prove API delivery. The reconciliation algorithm is tested, but scheduled runtime sources currently return no records.

## Outbound-webhook operations

- Create/update endpoints only with tenant-authorized API actions.
- Disable an endpoint to contain an incident without deleting its history.
- Treat newly issued and pending signing secrets as sensitive; never put them in logs or tickets.
- Rotation initiation does not by itself complete active-secret cutover.
- Investigate retries using delivery ID, event ID, tenant ID, correlation ID, attempt count, HTTP status, and error category rather than payload secrets.

## Docker Compose local VM profile

`compose.yml` starts PostgreSQL 16, Redis 7, the API, BillingService, and NGINX. It persists PostgreSQL data and BillingService workflow state. NGINX publishes port 80:

- `http://localhost/health` routes to the API.
- `http://localhost/billing/health` and `/billing/metrics` route to BillingService with the `/billing/` prefix removed.

Compose expects these runtime files outside Git:

```text
/etc/multitenant-saas-api/db.env
/etc/multitenant-saas-api/redis.env
/etc/multitenant-saas-api/api.env
/etc/multitenant-saas-api/billing.env
```

Copy and edit the placeholder templates under `deploy/env-examples/`. Never commit populated copies.

```bash
docker compose config
docker compose up --build
```

This profile uses HTTP ingress and is intended for a local Ubuntu VM. TLS, public DNS, hardened CORS, external secret storage, backup/restore, production telemetry, and rollback rehearsals are not completed by running Compose.

## CORS warning

`InitialExplicitCorsPolicy` currently permits any origin, header, and method. This is explicit but intentionally permissive for pre-deployment development. Define an environment-specific allowlist before exposing a browser client publicly.

## Troubleshooting order

1. **Restore/install:** rerun the failing `dotnet restore` or `npm ci` directly.
2. **EF tooling/migration:** verify the EF tool path and PostgreSQL connection, then rerun database update.
3. **Startup:** inspect `.local-api.log` and `.local-billing.log`; fix the first exception.
4. **Port mismatch:** keep `API_URL` and `BILLING_URL` overrides consistent between run and smoke shells.
5. **Health/smoke:** verify both processes remain running and query their health endpoints directly.
6. **Tests:** rerun the exact failing service command before rerunning the full wrapper.
