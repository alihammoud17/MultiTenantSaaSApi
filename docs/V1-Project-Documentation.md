# MultiTenantSaaSApi — V1 Project Documentation

## 1) Executive Summary

`MultiTenantSaaSApi` is an ASP.NET Core 8 Web API that implements a **Version 1 multi-tenant SaaS backend foundation**.

At this stage, the project delivers:

- Tenant onboarding (register tenant + admin user + default subscription).
- Tenant user authentication with JWT.
- Tenant context resolution for protected endpoints.
- Plan catalog and tenant plan upgrade.
- Per-tenant API rate limiting based on subscription plan.
- Tenant-scoped audit logging and retrieval.
- Health check endpoint for operational monitoring.

This V1 is designed as a production-oriented baseline for expanding into full SaaS capabilities (billing integrations, user management, product modules, analytics, etc.).

---

## 2) Goals of V1

The primary goals achieved so far are:

1. **Establish multi-tenant boundaries** through a tenant context model.
2. **Secure API access** with JWT authentication.
3. **Model SaaS commercial tiers** using plans + subscriptions.
4. **Protect platform usage** using monthly per-tenant rate limits.
5. **Create traceability** through audit logs.
6. **Provide confidence** through automated unit and integration tests.

---

## 3) High-Level Architecture

The solution is organized into clean layers:

- **Presentation**: API host, controllers, middleware, startup wiring.
- **Application**: business services (JWT, rate limiting, auditing).
- **Domain**: entities, contracts/interfaces, request/response DTOs.
- **Infrastructure**: EF Core DbContext, tenant context implementation, migrations.
- **Tests**: integration tests for endpoints + unit tests for core services.

This structure separates concerns and makes future extensions easier.

---

## 4) Core Domain Model (V1)

### Tenant
Represents a customer account in the SaaS platform.
- Includes `Name`, `Subdomain`, `Status`, and timestamps.
- Has one subscription and many users.

### User
Represents an authenticated user inside a tenant.
- Linked to `TenantId`.
- Stores email, hashed password, role, and last login info.

### Plan
Defines commercial tiers.
- V1 seeds:
  - `plan-free` (1,000 API calls/month, 1 user, $0)
  - `plan-pro` (50,000 API calls/month, 10 users, $99)

### Subscription
Represents a tenant’s currently active plan and billing period window.
- One subscription per tenant.

### AuditLog
Captures tenant-scoped actions for traceability.
- Stores action, entity, change payload, timestamp, user, and IP address.

---

## 5) Implemented API Surface (V1)

## Authentication

### `POST /api/auth/register`
Creates:
- tenant,
- admin user,
- default free subscription,
and returns a JWT authentication response.

Validation currently includes:
- unique tenant subdomain,
- unique admin email.

### `POST /api/auth/login`
Authenticates user by email/password (BCrypt verification), checks tenant status, updates last login timestamp, logs audit event, and returns JWT.

## Plans

### `GET /api/plans`
Public endpoint returning active plan catalog.

### `POST /api/plans/upgrade`
Protected endpoint that:
- validates requested plan,
- resolves the current tenant,
- updates subscription plan and period,
- records an audit event.

## Audit Logs

### `GET /api/tenant/audit-logs`
Protected endpoint to retrieve tenant audit events with filters:
- pagination (`page`, `pageSize`),
- action filter,
- date-range filters (`fromUtc`, `toUtc`).

## Operations

### `GET /health`
Health check endpoint for uptime/readiness checks.

---

## 6) Authentication and Tenant Resolution Strategy

V1 authentication and tenant context are implemented as follows:

1. **JWT generation** includes `tenant_id` and role claims.
2. **JWT bearer authentication** validates issuer, audience, lifetime, and signature.
3. **Tenant middleware** resolves tenant in this priority order:
   - subdomain,
   - `X-Tenant-ID` header,
   - `tenant_id` JWT claim.
4. Middleware blocks requests when tenant is missing, unknown, or suspended.

This gives flexibility for local development and future deployment models.

---

## 7) Rate Limiting (Plan-Aware)

Rate limiting is enforced via middleware + Redis:

- A per-tenant monthly key is tracked in Redis: `ratelimit:{tenantId}:{yyyy-MM}`.
- The limit value is read from the tenant’s current plan.
- Every protected request increments usage.
- Response headers expose limit, remaining calls, and reset timestamp.
- Exceeded tenants receive HTTP `429` with upgrade guidance.

This creates a direct connection between commercial plan and technical usage limits.

---

## 8) Audit Logging (Tenant-Scoped)

V1 audit logging records important events such as:

- tenant registration,
- user login,
- plan upgrades.

Each record includes tenant ID, user ID (if available), action, entity details, serialized changes, timestamp, and caller IP. Retrieval is tenant scoped with paging and filtering.

---

## 9) Persistence and Infrastructure

- **Database**: PostgreSQL (EF Core + Npgsql).
- **Cache/counters**: Redis (StackExchange.Redis).
- **ORM mapping**:
  - unique subdomain,
  - unique user email within tenant,
  - one subscription per tenant.
- **Seed data**: Free + Pro plans are inserted by model seed configuration.

Local configuration is intended to be stored via **.NET User Secrets**, not committed settings.

---

## 10) Security Decisions in V1

- Passwords are hashed with BCrypt.
- JWT secret, issuer, audience, and expiration are externally configurable.
- Protected endpoints use `[Authorize]`.
- Tenant middleware prevents cross-tenant access by enforcing tenant resolution and active status.

---

## 11) Quality and Testing Status

The repository includes both integration and unit tests.

### Integration coverage includes:
- health endpoint success,
- registration success with token return,
- login success for registered users,
- public plans retrieval,
- protected endpoint unauthorized behavior,
- plan upgrade success,
- audit logs retrieval and expected action capture.

### Unit coverage includes:
- audit service write/read behavior,
- rate limit service allowance/denial scenarios.

This test baseline validates the main V1 user and tenant flows.

---

## 12) How to Use the App (V1 Walkthrough)

## Prerequisites
- .NET 8 SDK
- PostgreSQL 16
- Redis 7

## Configure local secrets (from `Presentation/`)

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=saasapi;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "Jwt:Secret" "YOUR_256_BIT_SECRET"
dotnet user-secrets set "Jwt:Issuer" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:Audience" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:ExpirationMinutes" "60"
```

## Run API

```bash
dotnet run --project Presentation
```

## Typical API flow

1. Call `POST /api/auth/register` to create tenant + admin account and receive token.
2. Use returned JWT as `Authorization: Bearer <token>`.
3. Call `GET /api/plans` to view available plans.
4. Call `POST /api/plans/upgrade` to move tenant to another plan.
5. Call `GET /api/tenant/audit-logs` to view tenant activity history.

Swagger is available in development mode.

---

## 13) Current V1 Scope vs Next Iterations

### Included in V1
- Tenant registration/authentication.
- Plan model and upgrade flow.
- Tenant context enforcement middleware.
- Plan-based API usage limits.
- Tenant-scoped audit logs.
- Health checks and automated tests.

### Suggested V2+ improvements
- Refresh tokens and token revocation.
- Role/permission model beyond static role string.
- Billing provider integration (Stripe/Paddle/etc.).
- Background jobs for subscription lifecycle events.
- Admin portal endpoints for tenant/user management.
- Observability stack (metrics/traces dashboards).
- Expanded domain modules (products, usage analytics, invoices).

---

## 14) Conclusion

This V1 provides a strong, practical foundation for a multi-tenant SaaS backend with clear tenant boundaries, secure auth, commercial plan logic, usage control, and auditable actions. It is suitable for early-stage product validation and ready for iterative expansion in V2.
