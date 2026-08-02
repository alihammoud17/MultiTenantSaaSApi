# Decisions and Open Review Items

This file records durable decisions and explicitly unresolved operational choices. Historical proposals are retained under [archive](archive/README.md).

## Accepted decisions

### The .NET API is the system of record

Tenant identity, users, RBAC, business data, plans, entitlements, audit data, and internal subscription state belong to the API and PostgreSQL. BillingService may mirror provider-facing identifiers but does not independently authorize tenant access or own unrelated domain state.

### BillingService isolates provider concerns

Provider adapters, provider webhook verification, normalization, retry-safe provider work, and reconciliation belong in BillingService. Provider-specific payloads and status names must be normalized before crossing the internal contract.

### Contracts are authenticated and versioned

Internal billing callbacks use a versioned DTO and timestamped HMAC over the raw body. Contract changes require coordinated producer and consumer tests. Tenant and subscription mapping is validated internally rather than trusted from provider input.

### Public APIs use URL-segment versions

Public API routes use `/api/v1/...`. A new major API version is reserved for breaking contract changes. Backward-compatible additions do not require a new major version. Operational endpoints such as `/health` and `/metrics` are not public business API versions.

Application releases should use Semantic Versioning. Branch names are workflow conventions; release tags, not branch names, identify releases.

### Local validation is the present maturity target

The current target is deterministic, production-like local validation before deployment. “Implemented” means code and automated local tests exist; it does not imply production certification, deployment exercise, load validation, or on-call readiness.

### Secrets stay outside the repository

No real credentials belong in Git. Local user secrets or environment variables are used for development. The Compose profile expects runtime env files under `/etc/multitenant-saas-api/`; checked-in examples contain placeholders only.

### Current billing durability is file-backed

BillingService uses a JSON workflow state file for deterministic retry, deduplication, dead-letter, and restart-recovery tests. This is not a distributed queue and assumes controlled single-instance file access.

### Current observability is local JSON and structured logging

Both services expose local JSON health/metrics and correlation-aware structured logs. Production exporter, backend, dashboards, alerts, paging, and SLO implementation are deferred rather than implied.

### CORS is temporarily permissive

`InitialExplicitCorsPolicy` allows any origin, header, and method for local/pre-deployment clients. It must be replaced with an environment-appropriate allowlist before public browser exposure.

## Open owner-review items

1. Select the first deployment topology, environment count, hosts, and release/promotion process.
2. Decide whether the first deployment is internal-only or public/customer-facing.
3. Select public DNS, TLS termination, and certificate ownership.
4. Decide whether a browser client is in scope and define the production CORS allowlist.
5. Select the runtime secret store and rotation process.
6. Decide whether live billing is required for the first deployment and confirm the first provider.
7. Wire and validate real provider webhook verification, callback transport, and reconciliation data sources when live billing enters scope.
8. Decide whether the file-backed BillingService queue is acceptable for the first environment or replace it with durable shared infrastructure.
9. Select production telemetry export, storage, dashboard, alert, and SLO ownership.
10. Define migration rollback, database backup/restore, disaster recovery, and incident procedures.
11. Decide whether Redis must participate in API readiness checks.
12. Complete and rehearse outbound-webhook active-secret rotation cutover.
