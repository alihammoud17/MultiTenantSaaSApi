# Multi-Tenant SaaS API

A production-oriented, pre-deployment multi-tenant SaaS platform. The ASP.NET Core API is the system of record for tenants, identity, authorization, business data, entitlements, and internal subscription state. `BillingService` is a Node.js/TypeScript companion for provider-facing billing workflows.

The repository is designed for deterministic local validation. It is **not yet a production-certified deployment**: live provider webhook verification and authenticated BillingService-to-API delivery are not wired end to end, and production telemetry and recovery procedures remain unresolved.

## Implemented capabilities

- Tenant registration with email verification (registration returns `202` with no tokens; the tenant activates when its admin verifies, then logs in), JWT authentication, refresh-token rotation/revocation, identity verification/reset flows, MFA, and step-up sessions. User email is globally unique because registration and login identify users by email without a tenant discriminator; tenant-local email access remains indexed by tenant and email.
- Tenant-scoped RBAC, administrative user management, audit logs, and explicit tenant resolution and mismatch rejection.
- Plan, subscription, entitlement, add-on, override, usage-analytics, and tenant billing read/self-service foundations.
- Query-aligned PostgreSQL indexes for tenant audit history, action-filtered audit analytics, billing invoice history, and email-only authentication lookups.
- Signed, versioned, idempotent internal billing callbacks applied by the .NET API.
- Tenant outbound-webhook endpoint management and file/database-backed delivery, retry, signing, and replay protection.
- An MCP endpoint (`POST /api/v1/mcp`) with four read-only, tenant-scoped tools (usage summary, audit logs, webhook endpoints, CORS origins) for AI clients such as Claude Code, protected by the same authentication, tenant resolution, rate limit, RBAC and entitlement checks as the REST API.
- BillingService placeholder webhook ingestion, normalized events, a file-backed retry/dead-letter queue, reconciliation logic, and a tested Stripe gateway slice.
- Local JSON health and metrics endpoints, structured correlation-aware logs, and security/contract regression tests.

See [Architecture](docs/architecture.md), [Contracts](docs/contracts.md), and [Decisions](docs/decisions.md) for boundaries and limitations.

## Repository layout

```text
Presentation/    ASP.NET Core host, controllers, MCP endpoint, middleware, authorization, health and metrics
Application/     Authentication, RBAC, billing, entitlements, audit and workflow services
Domain/          Entities, DTOs, interfaces, responses and authorization constants
Infrastructure/  EF Core DbContext, mappings and migrations
Tests/           .NET unit and integration tests
BillingService/  Node.js/TypeScript provider-facing companion service
docs/            Durable architecture, operations, contracts, decisions and history
scripts/         Deterministic local bootstrap, run, smoke and test workflows
deploy/          Dockerfiles, NGINX and env templates for Compose; Terraform and Ansible for the v1 EC2 deployment
```

## Prerequisites

- .NET 10 SDK
- Node.js 22 or later and npm
- PostgreSQL 16 or later
- Redis 7 or later
- EF Core CLI at `/tmp/dotnet-tools/dotnet-ef` (or `DOTNET_EF_BIN`) for the repository scripts' migration steps, or an equivalent `dotnet ef` installation for manual use; scripts skip those steps with a warning without it

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

Use `scripts/dev.sh reset` before `seed` when a clean local database and workflow state are required. Run `scripts/dev.sh help` for the command index. `run` expects PostgreSQL and Redis to be reachable already. The smoke check proves local endpoint reachability, including the placeholder webhook endpoint (which rejects every event with `202`); it does not prove a live provider-to-API flow.

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
- MCP endpoint for AI clients: `POST /api/v1/mcp` (Streamable HTTP, bearer JWT); see [below](#using-the-mcp-endpoint-from-claude-code) and [Service contracts](docs/contracts.md#mcp-endpoint).
- API diagnostics: `GET /health` and `GET /metrics`.
- BillingService diagnostics: `GET /health` and `GET /metrics`.
- BillingService provider entry point: `POST /webhooks/provider` (placeholder runtime behavior).
- Swagger UI is available in the Development and Testing environments.

The API health endpoint checks the process and PostgreSQL, but not Redis. The metrics endpoints expose in-memory JSON snapshots; they are not production telemetry exporters.

## Using the MCP endpoint from Claude Code

The MCP endpoint takes the same access token as the REST API (`POST /api/v1/auth/login` returns it as `token`; it expires after `Jwt:ExpirationMinutes`). The tools act for that token's tenant and user only.

```bash
# API on http://localhost:5238 (needs PostgreSQL and Redis). Use the http profile:
# Claude Code may not trust the local HTTPS development certificate.
dotnet run --project Presentation --launch-profile http

claude mcp add --transport http saas-api http://localhost:5238/api/v1/mcp \
  --header "Authorization: Bearer <access-token>"
```

In Claude Code, `/mcp` then shows `saas-api` and its tools: all four for a tenant admin, fewer for users without the matching permissions.

- Keep the default local scope. Never put a token in a committed `.mcp.json`.
- The token expires. Instead of a fixed `--header`, a `headersHelper` script can log in and print `{"Authorization": "Bearer …"}` on each connection (see the [Claude Code MCP docs](https://code.claude.com/docs/en/mcp)). Keep the script and its credentials outside the repository.
- Locally, verification email is not delivered, so activate a tenant you registered yourself first: see [Pending tenant registrations](docs/operations.md#pending-tenant-registrations).
- Each MCP request counts against the tenant's monthly plan limit, and each tool call is recorded in its audit log as `MCP_TOOL_CALLED`.
- Other MCP clients, such as the MCP Inspector, connect the same way: Streamable HTTP to the URL above with the `Authorization` header. The server has no stdio transport.

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
- API CORS is permissive in `Development` only. Every other environment allows the origins in `Cors:AllowedOrigins` (`Cors__AllowedOrigins`, none by default, with credentials) plus exact `https` origins that active tenants register through `/api/v1/admin/tenant/cors-origins` (without credentials); see [Operations](docs/operations.md#cors). The production allowlist itself is still an owner decision.
- The MCP endpoint offers read tools only and has no MCP OAuth discovery; clients must be given an access token.
- There is no deployed OpenTelemetry exporter, telemetry backend, alerting/SLO practice, or deployment-proven backup/restore process.
- The v1 deployment (one EC2 host, Terraform + Ansible) is verified against the Floci emulator and a local systemd container, not yet on real AWS. TLS/DNS, environment count, browser-client scope, provider scope, and secret-store choices require owner decisions.

## Documentation

- [Architecture](docs/architecture.md)
- [Operations and configuration](docs/operations.md)
- [Service contracts](docs/contracts.md)
- [Architecture and operational decisions](docs/decisions.md)
- [Deployment roadmap](docs/deployment-roadmap.md)
- [Changelog](docs/CHANGELOG.md)
- [Historical documentation archive](docs/archive/README.md)
- [BillingService guide](BillingService/README.md)

## License

See [LICENSE.txt](LICENSE.txt).
