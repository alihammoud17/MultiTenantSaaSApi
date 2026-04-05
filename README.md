# Multi-Tenant SaaS API

ASP.NET Core 8 Web API for a multi-tenant SaaS platform.
The .NET API is the current system of record for tenant identity, authorization, subscription state, tenant-scoped operations, and internal billing lifecycle state updates.

## Project status

- **V2 is complete** in the current repository state.
- **V3 is the next phase** and is focused on productionizing billing, improving operational durability, strengthening security, and expanding platform maturity.
- The .NET API already contains the internal billing callback contract and subscription lifecycle application logic needed for that next phase.
- `BillingService/` exists as the provider-facing billing companion service, but it is still in a pre-live scaffold state and does **not** yet implement live provider integrations.

## Repository overview

This repository currently contains:

- a production-focused .NET API that handles tenant registration, authentication, authorization, plan enforcement, audit logging, admin operations, observability basics, and internal billing callbacks
- a Node.js `BillingService/` companion service scaffold with durable workflow orchestration and drift-aware reconciliation scaffolding for provider-facing billing integration

## Architecture summary

- **Presentation** (`Presentation/`): API host, middleware, auth wiring, observability, authorization policies, and controllers.
- **Application** (`Application/`): service-layer implementations for JWT issuance, refresh tokens, RBAC authorization, audit logging, rate limiting, internal signature validation, and billing callback processing.
- **Domain** (`Domain/`): entities, DTOs, contracts, interfaces, outputs, and authorization constants.
- **Infrastructure** (`Infrastructure/`): EF Core `DbContext`, schema mappings, tenant context persistence, and migrations.
- **Tests** (`Tests/`): integration and unit test coverage for auth, admin, audit, RBAC, observability, and billing callback flows.
- **BillingService** (`BillingService/`): provider-facing billing scaffold with placeholder webhook handling, normalized event types, durable file-backed workflow queueing, retry/backoff, dead-letter handling, and reconciliation summary skeletons.

## Implemented platform scope (V2 complete)

### Multi-tenant foundation

- Tenant registration with automatic tenant, admin user, and starter subscription creation.
- Tenant-aware request resolution using:
  - subdomain lookup
  - `X-Tenant-ID` header fallback
  - JWT `tenant_id` claim fallback
- Active-tenant enforcement that blocks requests for missing, unknown, or suspended tenants.
- Tenant-scoped persistence for users, subscriptions, RBAC assignments, refresh tokens, and audit logs.

### Authentication and token lifecycle

- JWT Bearer authentication for API access.
- Login and tenant registration endpoints.
- Refresh token issuance on register/login.
- Refresh token rotation on `POST /api/auth/refresh`.
- Refresh token revocation via:
  - `POST /api/auth/logout`
  - `POST /api/auth/revoke`
- Tenant-aware refresh token validation that prevents cross-tenant token reuse.
- Password hashing with BCrypt.

### Authorization and tenant administration

- Policy-based RBAC authorization built on permissions.
- Legacy `ADMIN` compatibility that still grants full access.
- Tenant admin endpoints for:
  - reading current tenant details
  - listing tenant users
  - adding tenant users
  - changing a tenant user's role / RBAC assignment
  - deleting tenant users
  - reading tenant audit logs
- Dedicated tenant audit log endpoint at `GET /api/tenant/audit-logs`.

### Plans and subscription management

- Public plan catalog endpoint at `GET /api/plans`.
- RBAC-protected plan upgrade endpoint at `POST /api/plans/upgrade`.
- Subscription records that track:
  - current plan
  - active / grace-period / canceled / expired lifecycle state
  - current billing period boundaries
  - scheduled downgrade target and effective date
  - cancellation timestamp
  - grace-period expiration timestamp

### Plan-based usage enforcement

- Per-tenant API usage limiting based on the current subscription plan.
- Rate-limit response headers:
  - `X-RateLimit-Limit`
  - `X-RateLimit-Remaining`
  - `X-RateLimit-Reset`
- Redis-backed monthly request counters.
- Safe fallback behavior that logs Redis connectivity problems and allows requests instead of failing the API outright.

### Audit logging

- Structured tenant-scoped audit log persistence.
- Audit records include tenant id, actor id, action, entity type/id, change payload, timestamp, and source IP.
- Audit events are emitted for key flows such as:
  - tenant registration
  - login
  - refresh token usage
  - logout / token revocation
  - plan changes
  - tenant user administration

### Internal billing callback support already implemented in the .NET API

The external billing provider integration is **not** implemented yet, but the .NET API already supports authenticated internal subscription-event callbacks from a future billing service.

Implemented callback capabilities:

- Authenticated internal endpoint at `POST /api/internal/billing/subscription-events`.
- Internal billing callback requests bypass tenant-resolution and tenant plan rate-limiting middleware; the callback pipeline validates tenant/subscription mapping from the signed payload instead.
- HMAC SHA-256 signature validation using:
  - `X-Billing-Timestamp`
  - `X-Billing-Signature`
- Clock-skew protection for callback timestamps.
- Explicit internal billing contract version validation.
- Safe tenant/subscription mapping validation before applying an event.
- Event idempotency using a persisted billing inbox table keyed by event id.
- Lifecycle handling for internal events including:
  - `subscription.activated`
  - `subscription.renewed`
  - `subscription.plan_changed`
  - `subscription.downgrade_scheduled`
  - `subscription.canceled`
  - `subscription.grace_period_started`
  - `invoice.payment_failed`
  - `subscription.grace_period_expired`
  - `subscription.expired`
- Subscription lifecycle updates that keep scheduled downgrades, grace periods, cancellations, and plan changes explicit and traceable.

### Observability and diagnostics

- Swagger UI in Development.
- JSON health endpoint at `GET /health` with per-check details.
- JSON metrics snapshot at `GET /metrics`.
- Structured request completion logging with correlation and trace identifiers.
- `X-Correlation-ID` request/response propagation.
- Per-request `ActivitySource` instrumentation (`multi-tenant-saas-api`) for future tracing exporters.
- Database connectivity health check.

### Automated testing coverage

The repository includes automated tests covering:

- health and metrics endpoints
- tenant registration and login
- refresh token rotation, logout, and revocation flows
- tenant-scoped audit log retrieval
- tenant admin user-management behavior
- RBAC permission evaluation and authorization handler behavior
- internal billing callback validation, lifecycle handling, cross-tenant rejection, and idempotency
- deeper security-focused scenarios for authentication negatives, authorization denials, tenant-isolation tampering, input validation abuse cases, and internal billing signature hardening

## V3 work planned next

The next phase is intentionally separate from the already-implemented V2 platform scope.

### Billing and workflow priorities

- connect `BillingService` to the .NET internal billing callback endpoint
- replace placeholder webhook handling with real provider webhook verification and normalization
- operationalize the durable retry, replay protection, dead-letter, and reconciliation workflows now scaffolded in `BillingService`
- keep provider-specific logic inside `BillingService` while the .NET API remains the system of record

### Platform maturity priorities

- tenant-facing billing self-service capabilities built on internal subscription state
- entitlements / add-ons / feature gating
- stronger security hardening and operational diagnostics
- usage analytics and outbound webhooks

### Work that is **not** complete yet

The repository should still be treated as **pre-live for provider billing integration**:

- no live billing provider SDK integration is implemented
- no verified external webhook ingestion flow is implemented
- `BillingService` does not yet call the .NET internal callback endpoint
- `BillingService` now includes drift-aware reconciliation logic, but it is not yet connected to live provider/.NET state readers
- end-to-end provider synchronization across both services is not yet implemented


## Operational notes (durable workflow iteration)

The current durable workflow iteration adds operational primitives in `BillingService` that are designed to survive restarts and reduce duplicate processing risk while live provider integration is still pending:

- file-backed workflow state persistence for queued work, retry metadata, and dead-lettered events
- replay-safe deduplication keyed by normalized `eventId`
- retry with bounded exponential backoff and max-attempt dead-lettering
- reconciliation comparison job scaffolding that can detect provider/internal drift once live readers are configured

These capabilities improve service resilience, but they are still **pre-live** because webhook verification, provider SDK calls, and authenticated callback delivery to the .NET API are not yet wired end-to-end.

For operational procedures (startup checks, state-file hygiene, replay handling, dead-letter triage, and reconciliation troubleshooting), use:

- `docs/Billing-Workflow-Runbook.md`

## API surface summary

### Public/auth endpoints

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `POST /api/auth/revoke` (authenticated + RBAC-protected)
- `GET /api/plans`
- `GET /health`
- `GET /metrics`

### Tenant/admin endpoints

Under `api/admin/tenant`:

- `GET /api/admin/tenant`
- `GET /api/admin/tenant/users`
- `POST /api/admin/tenant/users`
- `PUT /api/admin/tenant/users/{userId}/role`
- `DELETE /api/admin/tenant/users/{userId}`
- `GET /api/admin/tenant/audit-logs`

Additional tenant-scoped endpoint:

- `GET /api/tenant/audit-logs`

### Internal service endpoint

- `POST /api/internal/billing/subscription-events`

## Local setup and run

### Prerequisites

- .NET 8 SDK
- PostgreSQL 16+
- Redis 7+

### Configure secrets

Run from `Presentation/`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=saasapi;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "Jwt:Secret" "YOUR_256_BIT_SECRET"
dotnet user-secrets set "Jwt:Issuer" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:Audience" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:ExpirationMinutes" "60"
dotnet user-secrets set "BillingIntegration:SharedSecret" "YOUR_INTERNAL_BILLING_SHARED_SECRET"
dotnet user-secrets set "BillingIntegration:AllowedClockSkewMinutes" "5"
```

Optional verification:

```bash
dotnet user-secrets list
```

### Apply database migrations

From repository root:

```bash
dotnet ef database update --project Infrastructure --startup-project Presentation
```

### Run the API

From repository root:

```bash
dotnet run --project Presentation
```

Swagger UI is enabled in Development.

### Run BillingService (durable workflow iteration scaffold)

From repository root:

```bash
cd BillingService
npm install
npm run dev
```

Optional environment overrides for durability/reconciliation behavior:

```bash
export WORKFLOW_STATE_PATH="/tmp/billing-workflow-state.json"
export WORKFLOW_MAX_ATTEMPTS=3
export WORKFLOW_INITIAL_BACKOFF_MS=1000
export WORKFLOW_MAX_BACKOFF_MS=30000
export WORKFLOW_POLL_INTERVAL_MS=2000
export RECONCILIATION_INTERVAL_MS=300000
```

Notes:

- `BillingService` is still pre-live for provider integration and .NET callback delivery.
- Use `docs/Billing-Workflow-Runbook.md` for dead-letter, replay, and reconciliation operating procedures.

## Testing

Run directly from the repository root:

```bash
dotnet build MultiTenantSaaSApi.sln
dotnet test MultiTenantSaaSApi.sln
```

BillingService validation commands:

```bash
cd BillingService
npm run build
npm test
```

## Additional docs

- `BillingService/README.md`
- `docs/V3-Implementation-Backlog.md`
- `docs/Internal-Billing-Contract.md`
- `docs/Billing-Workflow-Runbook.md`
