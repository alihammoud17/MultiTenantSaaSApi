# Multi-Tenant SaaS API

ASP.NET Core 8 Web API for a multi-tenant SaaS platform.
The .NET API is the current system of record for tenant identity, authorization, subscription state, tenant-scoped operations, and internal billing lifecycle state updates.

## Project overview

This repository currently contains:

- a production-focused .NET API that already handles tenant registration, authentication, authorization, plan enforcement, audit logging, admin operations, and internal billing callbacks
- a minimal `BillingService/` TypeScript companion scaffold reserved for future provider-facing billing/webhook workflow work

The .NET API remains the system of record. The Node.js billing service is still a scaffold, but the .NET side already contains the internal callback contract, signature validation, idempotency storage, and subscription lifecycle application logic needed for the next billing phase.

## Architecture summary

- **Presentation** (`Presentation/`): API host, middleware, auth wiring, observability, authorization policies, and controllers.
- **Application** (`Application/`): service-layer implementations for JWT issuance, refresh tokens, RBAC authorization, audit logging, rate limiting, internal signature validation, and billing callback processing.
- **Domain** (`Domain/`): entities, DTOs, contracts, interfaces, outputs, and authorization constants.
- **Infrastructure** (`Infrastructure/`): EF Core `DbContext`, schema mappings, tenant context persistence, and migrations.
- **Tests** (`Tests/`): integration and unit test coverage for auth, admin, audit, RBAC, observability, and billing callback flows.
- **BillingService** (`BillingService/`): placeholder Node.js/TypeScript billing companion service scaffold.

## Current application features

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

## Testing

Run directly from the repository root:

```bash
dotnet build MultiTenantSaaSApi.sln
dotnet test MultiTenantSaaSApi.sln
```

## Current status and next phase

### Already implemented in the .NET API

- Multi-tenant auth and tenant enforcement
- Refresh token lifecycle support
- RBAC permissions and admin tenant management
- Plan upgrade flow and rate limiting
- Audit logging
- Observability baseline
- Internal billing callback validation and subscription lifecycle application

### Still planned / not implemented yet

- Live billing provider adapters
- External webhook ingestion in the Node.js billing service
- Background billing workflow orchestration in the Node.js billing service
- End-to-end provider synchronization across both services

## Additional docs

- `BillingService/README.md`
