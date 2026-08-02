# Multi-Tenant SaaS API

A production-oriented, pre-deployment multi-tenant SaaS platform. The ASP.NET Core API is the system of record for tenants, identity, authorization, business data, entitlements, and internal subscription state. `BillingService` is a Node.js/TypeScript companion for provider-facing billing workflows.

The repository is designed for deterministic local validation. It is **not yet a production-certified deployment**: live provider webhook verification and authenticated BillingService-to-API delivery are not wired end to end, CORS is intentionally permissive, and production telemetry and recovery procedures remain unresolved.

## Implemented capabilities

- Tenant registration and JWT authentication, refresh-token rotation/revocation, identity verification/reset flows, MFA, and step-up sessions.
- Tenant-scoped RBAC, administrative user management, audit logs, and explicit tenant resolution and mismatch rejection.
- Plan, subscription, entitlement, add-on, override, usage-analytics, and tenant billing read/self-service foundations.
- Signed, versioned, idempotent internal billing callbacks applied by the .NET API.
- Tenant outbound-webhook endpoint management and file/database-backed delivery, retry, signing, and replay protection.
- BillingService placeholder webhook ingestion, normalized events, a file-backed retry/dead-letter queue, reconciliation logic, and a tested Stripe gateway slice.
- Local JSON health and metrics endpoints, structured correlation-aware logs, and security/contract regression tests.

See [Architecture](docs/architecture.md), [Contracts](docs/contracts.md), and [Decisions](docs/decisions.md) for boundaries and limitations.

## Repository layout

```text
Presentation/    ASP.NET Core host, controllers, middleware, authorization, health and metrics
Application/     Authentication, RBAC, billing, entitlements, audit and workflow services
Domain/          Entities, DTOs, interfaces, responses and authorization constants
Infrastructure/  EF Core DbContext, mappings and migrations
Tests/           .NET unit and integration tests
BillingService/  Node.js/TypeScript provider-facing companion service
docs/            Durable architecture, operations, contracts, decisions and history
scripts/         Deterministic local bootstrap, run, smoke and test workflows
```

## Prerequisites

- .NET 10 SDK
- Node.js 22 or later and npm
- PostgreSQL 16 or later
- Redis 7 or later
- EF Core CLI at `/tmp/dotnet-tools/dotnet-ef` for the repository scripts, or an equivalent `dotnet ef` installation for manual use

Docker Compose can provide PostgreSQL, Redis, both services, and NGINX on a local Ubuntu VM. It expects runtime env files outside the repository under `/etc/multitenant-saas-api/`; see [Operations](docs/operations.md#docker-compose-local-vm-profile).

## Quick start

From the repository root:

```bash
scripts/dev.sh bootstrap
scripts/dev.sh seed
scripts/dev.sh run
```

In another shell:

```bash
scripts/dev.sh smoke
scripts/dev.sh test
```

Use `scripts/dev.sh reset` before `seed` when a clean local database and workflow state are required. Run `scripts/dev.sh help` for the command index. The smoke check proves local endpoint reachability and placeholder webhook acceptance; it does not prove a live provider-to-API flow.

Detailed setup, migration, configuration, direct test commands, troubleshooting, Compose ingress, and workflow recovery are in [Operations](docs/operations.md).

## Configuration summary

The API requires a PostgreSQL connection, Redis connection, strong JWT secret, and internal billing shared secret. ASP.NET Core environment-variable names use double underscores:

- `ConnectionStrings__DefaultConnection`
- `Redis__ConnectionString`
- `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpirationMinutes`
- `BillingIntegration__SharedSecret`, `BillingIntegration__AllowedClockSkewMinutes`

BillingService variables include `BILLING_PROVIDER`, `WEBHOOK_SIGNING_SECRET`, `DOTNET_CALLBACK_BASE_URL`, `STRIPE_API_KEY`, `STRIPE_API_BASE_URL`, and the `WORKFLOW_*` and `RECONCILIATION_INTERVAL_MS` controls. Configuration of these values does not make an unwired provider or callback path live. See the complete [configuration reference](docs/operations.md#configuration-reference) and [BillingService README](BillingService/README.md).

Never commit real secrets. Repository env examples contain placeholders only.

## Endpoints and API discovery

- Public API routes use URL-segment versioning under `/api/v1/...`.
- Internal billing callback: `POST /api/internal/billing/subscription-events`.
- API diagnostics: `GET /health` and `GET /metrics`.
- BillingService diagnostics: `GET /health` and `GET /metrics`.
- BillingService provider entry point: `POST /webhooks/provider` (placeholder runtime behavior).
- Swagger UI is available in the Development and Testing environments.

The API health endpoint checks the process and PostgreSQL, but not Redis. The metrics endpoints expose in-memory JSON snapshots; they are not production telemetry exporters.

## Testing

Run the standardized full validation:

```bash
scripts/dev.sh test
```

Or run the service-specific commands documented in [Operations](docs/operations.md#validation-and-tests). Coverage includes authentication and authorization security, tenant isolation, rate limiting, internal billing authentication/idempotency, entitlements, outbound webhooks, cross-service contracts, replay fixtures, workflow recovery, reconciliation, and the Stripe gateway.

## Current limitations

- The BillingService runtime selects a placeholder webhook adapter even when `BILLING_PROVIDER` is `stripe` or `paddle`.
- The Stripe gateway is implemented and tested but is not connected to the default runtime provider flow.
- The default callback publisher is a no-op logger, and default reconciliation sources return no live records.
- Billing workflow durability uses a local JSON file, not a distributed queue.
- API CORS currently allows any origin, header, and method; tighten it before public browser exposure.
- There is no deployed OpenTelemetry exporter, telemetry backend, alerting/SLO practice, or deployment-proven backup/restore process.
- Deployment topology, TLS/DNS, browser-client scope, provider scope, and secret-store choices require owner decisions.

## Documentation

- [Architecture](docs/architecture.md)
- [Operations and configuration](docs/operations.md)
- [Service contracts](docs/contracts.md)
- [Architecture and operational decisions](docs/decisions.md)
- [Changelog](docs/CHANGELOG.md)
- [Historical documentation archive](docs/archive/README.md)
- [BillingService guide](BillingService/README.md)

## License

See [LICENSE.txt](LICENSE.txt).
