# Multi-Tenant SaaS API

ASP.NET Core 8 Web API for a multi-tenant SaaS platform.  
The .NET API is the current system of record for tenant identity, authorization, subscription/plan state, and tenant-scoped operations.

## Project overview

This repository currently contains the .NET API and automated tests.  
A separate Node.js billing orchestration service is planned next, but billing provider integrations/webhooks are **not implemented yet** in this repo.

## Architecture summary

- **Presentation** (`Presentation/`): API host, middleware, auth wiring, authorization policies, controllers.
- **Application** (`Application/`): service-layer implementations (JWT, refresh tokens, RBAC authorization, audit logging, rate limiting).
- **Domain** (`Domain/`): entities, DTOs, contracts, interfaces, authorization constants.
- **Infrastructure** (`Infrastructure/`): EF Core `DbContext`, migrations, tenant context persistence.
- **Tests** (`Tests/`): integration and unit test projects.

## Implemented features

### V1 foundation (implemented)

- Tenant registration and login (`/api/auth/register`, `/api/auth/login`).
- Tenant context enforcement middleware.
- Plan catalog and tenant plan upgrade flow (`/api/plans`, `/api/plans/upgrade`).
- Plan-based API usage limiting.
- Tenant-scoped audit logging.
- Health checks (`/health`).
- Unit and integration tests.

### V2 progress through admin endpoints (implemented)

- Refresh token issuance, rotation, logout revocation, and explicit revocation endpoints.
- Role/permission-based authorization model (RBAC policies and permission checks).
- Admin endpoints for tenant/user management and tenant audit-log retrieval.

### Not implemented yet

- Billing provider adapters and webhook ingestion/verification.
- Subscription lifecycle orchestration in a separate Node.js billing service.

## Authentication and authorization

- API auth uses JWT Bearer tokens.
- Access tokens include tenant context claims used by middleware.
- Refresh token workflow endpoints:
  - `POST /api/auth/refresh`
  - `POST /api/auth/logout`
  - `POST /api/auth/revoke` (RBAC-protected)
- Authorization uses policy-based RBAC (for example: users manage/read, tenants read, billing manage, audit logs read).

## Multi-tenancy model

- Tenant is the primary boundary for users, subscriptions, roles, and audit logs.
- Tenant context is resolved per request and used to scope protected operations.
- Tenant-scoped APIs filter data by tenant context to prevent cross-tenant access.

## Admin capabilities (current)

Under `api/admin/tenant` (authenticated + RBAC-protected):

- `GET /api/admin/tenant` – current tenant details.
- `GET /api/admin/tenant/users` – list users for current tenant.
- `POST /api/admin/tenant/users` – create/invite tenant user.
- `PUT /api/admin/tenant/users/{userId}/role` – assign/change role.
- `DELETE /api/admin/tenant/users/{userId}` – remove tenant user.
- `GET /api/admin/tenant/audit-logs` – query tenant audit logs.

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

Run the standard restore/build/test script:

```bash
./scripts/run-tests.sh
```

Or run directly:

```bash
dotnet test MultiTenantSaaSApi.sln
```

## Roadmap / next steps

### Completed to date

- V1 multi-tenant API foundation.
- V2 security/auth upgrades (refresh tokens + revocation).
- V2 RBAC and admin tenant/user management endpoints.

### Next phase (planned)

- Introduce Node.js billing orchestration service.
- Add billing provider integration via adapters.
- Add authenticated webhook ingestion, idempotent event processing, and subscription lifecycle handlers.
- Keep .NET API as source of truth for tenant/subscription state exposed to the platform.

## Additional docs

- `docs/V1-Project-Documentation.md`
- `docs/V2-Implementation-Backlog.md`
