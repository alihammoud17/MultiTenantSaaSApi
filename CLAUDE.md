# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read first

`AGENTS.md` (root) and `BillingService/AGENTS.md` are the authoritative working rules for this repo: system boundaries, tenant/billing safety rules, the required documentation workflow, and validation expectations. Follow them; this file only adds orientation. Historical plans under `docs/archive/` are not sources of truth — use `README.md`, `BillingService/README.md`, and `docs/{architecture,operations,contracts,decisions}.md` alongside the code.

## Commands

Standard local workflow (wraps `scripts/local/*.sh`):

```bash
scripts/dev.sh bootstrap   # deps, build, migrations
scripts/dev.sh seed        # re-apply migrations
scripts/dev.sh run         # API + BillingService in foreground
scripts/dev.sh smoke       # reachability + placeholder webhook acceptance only
scripts/dev.sh test        # full .NET + BillingService validation
scripts/dev.sh reset       # DROPS the local DB and workflow state — only when in scope
```

`scripts/dev.sh test` hard-requires `/tmp/dotnet-tools/dotnet-ef`, which may not exist on this machine; if it doesn't, run the steps directly. It also skips BillingService typecheck.

.NET (solution `MultiTenantSaaSApi.sln`):

```bash
dotnet restore && dotnet build --no-restore
dotnet test --no-build --verbosity normal
dotnet test --filter "FullyQualifiedName~TenantIsolationSecurityTests"   # single class
dotnet test --filter "FullyQualifiedName~Tests.Integration.InternalBillingSecurityTests.SomeMethod"
```

Tests use xUnit v3 + FluentAssertions. Integration tests (`Tests/Integration/ApiWebApplicationFactory.cs`) run the real host in the `Testing` environment with SQLite in-memory replacing PostgreSQL (`EnsureCreated`, not migrations) and an always-allow rate limiter replacing Redis — no local Postgres/Redis needed for `dotnet test`. Because of SQLite, Postgres-specific behavior (e.g. index/migration details) isn't exercised by integration tests.

EF Core migrations (DbContext `Infrastructure/Data/ApplicationDbContext.cs`, startup host `Presentation`):

```bash
dotnet ef migrations add <Name> --project Infrastructure --startup-project Presentation
dotnet ef database update --project Infrastructure --startup-project Presentation
```

Scripts accept `DOTNET_EF_BIN` to point at a different `dotnet-ef`.

BillingService (Node 22+, run from `BillingService/`):

```bash
npm ci
npm run build        # tsc -p tsconfig.build.json
npm run typecheck    # tsc --noEmit
npm test             # node --test --experimental-transform-types tests/*.test.ts
node --test --experimental-transform-types tests/webhookHandler.test.ts   # single file
npm run dev          # watch mode, runs TS directly via --experimental-strip-types
```

No bundler/test framework — Node's built-in test runner and native TS type stripping.

## Architecture

Two services with deliberately split ownership:

- **.NET API** — system of record for tenants, identity, RBAC, business data, plans/entitlements, audit, and internal subscription state. Clean-architecture layers: `Presentation` (host, controllers, middleware, authorization handlers, rate limiting, `OutboundWebhookDeliveryDispatcher` hosted service) → `Application` (services with the business orchestration, e.g. `AuthOrchestrationService`) → `Domain` (entities, DTOs, interfaces, authorization constants) ← `Infrastructure` (EF Core/PostgreSQL, migrations, tenant context). Keep controllers transport-only.
- **BillingService** (`BillingService/src`) — provider-facing Node/TS companion: `routes/` → `webhooks/` + `providers/` (adapters) → normalized events → `workflows/` (file-backed JSON queue with dedup/retry/dead-letter) → `jobs/` (callback publishing, reconciliation). Wired together in `app.ts`.

Cross-cutting mechanics that span multiple files:

- **Tenant resolution**: `TenantMiddleware` takes a hint from subdomain or `X-Tenant-ID`; an authenticated JWT's `tenant_id` claim is authoritative and a conflicting hint is rejected. Tenant must exist and be active. Isolation is additionally enforced by tenant predicates in queries and service-layer checks — every new query/endpoint needs explicit tenant scoping plus a tenant-isolation test.
- **Rate limiting**: fixed-window per-IP limiter on unauthenticated auth routes, and a separate plan-aware Redis-backed limiter for authenticated tenant traffic.
- **Entitlements**: assembled from plan mappings + active add-ons + tenant overrides with deterministic precedence; enforced only on selected surfaces.
- **Internal billing contract** (`docs/contracts.md`): `POST /api/internal/billing/subscription-events`, versioned body (`contractVersion`), HMAC-SHA256 over `<timestamp>.<raw body>` via `X-Billing-Timestamp`/`X-Billing-Signature`, keyed by `BillingIntegration:SharedSecret`, idempotent on event IDs. Producer (`BillingService/tests/billingCallbackContract.test.ts`) and consumer (`Tests/Integration/CrossServiceBillingContractTests.cs`) conformance tests must change together.
- Public API routes are URL-versioned under `/api/v1/...`.

## What is not live (don't claim otherwise)

- All `BILLING_PROVIDER` values select the placeholder webhook adapter (`BillingService/src/providers/index.ts`); no real signature verification.
- Default callback publisher is a no-op logger — BillingService does not actually call the API at runtime; the contract is exercised only in tests.
- Reconciliation runs against empty provider/internal readers; the Stripe gateway is a tested component not wired into the default flow.
- Health/metrics are local JSON/in-memory snapshots; API health checks PostgreSQL but not Redis.

## Workflow expectations

Per `AGENTS.md`: thin additive slices, no unrelated refactors, update affected docs (`README.md`, `docs/*.md`, `docs/CHANGELOG.md`, `BillingService/README.md`) in the same change, and report exactly which validation commands ran and their results. For docs-only changes, `git diff --check` plus verifying referenced paths/commands is sufficient. Secrets never go in the repo; runtime env files for the Compose/Ubuntu VM profile live under `/etc/multitenant-saas-api/`.
