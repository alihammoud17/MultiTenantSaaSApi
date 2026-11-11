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

### CORS is a configured allowlist outside Development

`ApiCorsPolicy` allows any origin without credentials in `Development` only. Every other environment allows only the origins in `Cors:AllowedOrigins`, with credentials, and allows none when it is unset. This fails closed: forgetting the setting blocks browser clients rather than exposing the API to every origin. Which origins belong in the production list is still open (item 4 below).

### Tenants can register their own CORS origins, without credentials

Active tenants can also register exact `https` origins (`TenantAllowedOrigins`), managed by tenant administrators and gated by the `feature.security.cors_origins.manage` entitlement. Preflights carry no credentials, so on a shared API host an origin is accepted if any active tenant registered it; on a tenant subdomain only that tenant's origins count. Since one tenant's origin is then accepted for all tenants' routes on a shared host, tenant origins never get `Access-Control-Allow-Credentials`; only `Cors:AllowedOrigins` does. This is safe while the API authenticates with bearer tokens only. Adding cookie-based authentication would need this revisited. The origin lists are cached in Redis and invalidated on change; see [Operations](operations.md#origin-cache).

## Open owner-review items

1. Select the environment count and the release/promotion process. The first topology is chosen: one Debian 12 EC2 host provisioned by Terraform and configured by Ansible with native systemd services, growing toward multi-region active-passive DR ([deployment roadmap](deployment-roadmap.md)). Still open: how many environments, and how a release is promoted between them.
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
