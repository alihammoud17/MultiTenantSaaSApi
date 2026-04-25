# Multi-Tenant SaaS API

ASP.NET Core 8 Web API for a multi-tenant SaaS platform.
The .NET API is the current system of record for tenant identity, authorization, subscription state, tenant-scoped operations, and internal billing lifecycle state updates.

## Project status

- **V1, V2, and V3 are complete** in the current repository state.
- **V4 execution is active for pre-deployment code-first maturity** in `docs/V4-Implementation-Backlog.md`.
- **P0 slices 1-4, the documentation-baseline slice, P1.1 entitlement matrix harness regression iteration, and P1.2 developer workflow hardening foundation are implemented** as of **April 25, 2026**.
- The .NET API remains the system of record for tenant identity, authorization, tenant-scoped business state, and internal subscription lifecycle state.
- `BillingService/` is now documented as a productionized billing companion service with explicit notes on what is implemented vs what remains design-only for post-V3 evolution.

## V4 orchestration profile (local-first, pre-deployment)

### Local bootstrap/reset/seed path (code-ready setup)

From the repository root:

```bash
scripts/local/bootstrap.sh
scripts/local/reset.sh   # optional clean-state reset
scripts/local/seed.sh    # explicit seed boundary
```

What this does today:

1. restores and builds the .NET solution
2. applies EF Core migrations when `/tmp/dotnet-tools/dotnet-ef` is available
3. installs BillingService dependencies with `npm ci`

`reset.sh` standardizes local cleanup + DB reset behavior:

1. removes `.local-api.log` / `.local-billing.log` and BillingService durable workflow state file
2. drops the local DB with `dotnet-ef database drop --force` (when `/tmp/dotnet-tools/dotnet-ef` exists)
3. reapplies migrations

`seed.sh` standardizes seed behavior for the current V4 stage:

1. reapplies EF migrations so model-managed seed data stays current
2. prints the explicit manual boundary for scenario/demo tenant seed data

### Local smoke/test path (code-ready runtime validation)

Run in this order from the repository root:

```bash
# shell A
scripts/local/run.sh

# shell B
scripts/local/smoke.sh
scripts/local/test.sh
```

Smoke currently validates only local runtime readiness for the orchestration profile:

- .NET API health endpoint responsiveness
- BillingService health endpoint responsiveness
- BillingService placeholder webhook endpoint acceptance

`test.sh` standardizes full local verification across both services:

- required .NET validation commands (`dotnet --info`, `dotnet-ef --version`, restore/build/test)
- BillingService dependency install, build, and test execution

For detailed script behavior, environment overrides, and troubleshooting, use `docs/Local-Orchestration-Profile.md`.

### Code-ready local validation vs production readiness

**Code-ready local validation (implemented now):**

- deterministic bootstrap and two-shell run/smoke workflow
- repeatable local service startup with captured logs
- basic smoke assertions over API/BillingService health and placeholder webhook acceptance

**Production readiness (not claimed in this iteration):**

- verified live provider webhook authenticity and end-to-end provider -> BillingService -> .NET callback flow
- deployment-proven telemetry exporters, dashboards, alerts, and SLO enforcement
- production operations hardening (incident automation, key-rotation processes, and environment-level DR exercises)

## V4 pre-deployment capability map (P0 baseline, April 19, 2026)

| Capability track | Pre-deployment status | Demoable locally today | Post-deployment remaining |
| --- | --- | --- | --- |
| Deterministic platform orchestration | Implemented | `scripts/local/bootstrap.sh`, `scripts/local/run.sh`, and `scripts/local/smoke.sh` demonstrate repeatable setup + runtime smoke flow. | Environment-specific deployment bootstrap/runbooks and production incident automation. |
| .NET internal billing callback contract safety | Implemented | Signed callback validation, version gates, required-field checks, tenant/subscription mapping validation, and duplicate-event idempotency can be validated via integration tests. | Live provider-origin event chain verification under real secrets and rotated key operations. |
| Cross-service contract conformance | Implemented | .NET consumer contract tests + BillingService producer contract tests prove agreed payload/version behavior in local CI/test runs. | Ongoing version-compatibility rollout policy across deployed service versions. |
| Billing replay/idempotency fixture validation | Implemented (pre-live fixture slice) | BillingService fixture-driven replay tests validate duplicate delivery safety, out-of-order processing behavior, stale timestamp handling behavior, and invalid-signature rejection pre-enqueue. | Production replay/forensics workflows against real provider delivery retries and incident datasets. |
| Tenant-isolation invariant coverage | Implemented | Integration tests assert cross-tenant rejection across admin, analytics, audit, billing read, and internal billing callback surfaces. | Continuous production verification with runtime telemetry alerts for isolation regressions. |
| Entitlement regression matrix harness (P1.1) | Implemented (foundation + first regression expansion) | .NET unit matrix tests now cover boolean/integer precedence, add-on increment merge behavior, override allow/deny precedence, endpoint-gated billing/admin/analytics entitlement keys, and representative lifecycle combinations (`Active`, `GracePeriod`, `Canceled`, `Expired`). | Broaden matrix dimensions (additional add-on merge modes and deeper endpoint combinations) in later P1 slices. |
| Provider webhook verification + callback delivery wiring | Partial / pre-live | Placeholder webhook acceptance and scaffolded provider boundaries are locally demoable. | Real provider signature verification and fully wired provider -> BillingService -> .NET callback flow in deployed environments. |
| Observability and operations hardening | Partial | Local health/metrics and structured logs are demoable for both services. | Deployment-proven exporters, dashboards, alerts, SLO/error-budget operation, and DR exercises. |

### What is demoable locally today (P0-complete baseline)

- full local orchestration bootstrap/run/smoke path for both services
- cross-service callback contract conformance checks in automated tests
- fixture-driven billing replay/idempotency validation scenarios in BillingService tests
- tenant-isolation invariant negatives across sensitive .NET API surfaces
- health + metrics + structured-log visibility suitable for deterministic local demonstrations

### What remains explicitly post-deployment

- live provider webhook signature verification in active runtime flows with production secrets
- end-to-end provider event delivery into authenticated .NET callbacks in deployed environments
- production-grade observability/exporter/alert/SLO maturity proven under real workloads
- operator automation for key rotation, incident response, and DR exercises

## Versioning

- Public API routes use URL-segment versioning (`/api/v1/...`).
- The first production public API version is **v1**.
- API major versions should change only for breaking contract changes.
- Application release versions follow SemVer (`1.0.0`, `1.0.1`, `1.1.0`, `2.0.0`).
- Branches are workflow-only (`master`, `feature/*`, `release/*`, `hotfix/*`) and do not control runtime API version selection.
- Production deployments should come from SemVer release tags.

## Feature matrix (current capabilities)

| Capability area | Current status (April 19, 2026) | Notes |
| --- | --- | --- |
| Multi-tenant auth + RBAC + audit | Implemented | Core tenant isolation, auth lifecycle, RBAC, and tenant audit surfaces are in production-ready shape. |
| Plan catalog + lifecycle state | Implemented | Plan upgrades and subscription lifecycle state are persisted in the .NET API. |
| Tenant billing self-service foundation | Implemented (foundation) | Tenant billing status/invoice reads and cancel/reactivate actions exist on internal state. |
| Internal billing callback contract (.NET) | Implemented | Signed callback ingestion, idempotency inbox, and lifecycle application are live in the API. |
| BillingService durable workflow scaffold | Implemented (pre-live) | Durable retry/dead-letter/reconciliation scaffolding exists, but live provider callback flow is still pending. |
| Provider webhook verification + live provider sync | Not implemented yet | BillingService remains pre-live for verified external webhook ingestion. |
| Entitlements model + feature gating | Implemented (progressive rollout) | Additive entitlement schema + seeded definitions/mappings are in place, with evaluator/enforcer-backed gates active for billing invoice reads, billing self-service mutations, plan upgrades, advanced admin user management, and tenant audit-log analytics access. |
| Usage analytics + outbound webhooks | Partially implemented (analytics + outbound delivery foundation) | Tenant-scoped usage aggregation/query service and a first outbound webhook foundation (signed payloads, retries, delivery status, idempotency key headers, and replay-safe event dedupe) are implemented. |

## Repository overview

This repository currently contains:

- a production-focused .NET API that handles tenant registration, authentication, authorization, plan enforcement, audit logging, admin operations, observability basics, and internal billing callbacks
- a Node.js `BillingService/` companion service scaffold with durable workflow orchestration, drift-aware reconciliation scaffolding, and an initial Stripe provider API gateway slice for tenant checkout/portal/invoice-sync calls

## Architecture summary

- **Presentation** (`Presentation/`): API host, middleware, auth wiring, observability, authorization policies, and controllers.
- **Application** (`Application/`): service-layer implementations for JWT issuance, refresh tokens, RBAC authorization, audit logging, rate limiting, internal signature validation, and billing callback processing.
- **Domain** (`Domain/`): entities, DTOs, contracts, interfaces, outputs, and authorization constants.
- **Infrastructure** (`Infrastructure/`): EF Core `DbContext`, schema mappings, tenant context persistence, and migrations.
- **Tests** (`Tests/`): integration and unit test coverage for auth, admin, audit, RBAC, observability, and billing callback flows.
- **BillingService** (`BillingService/`): provider-facing billing scaffold with placeholder webhook handling, normalized event types, durable file-backed workflow queueing, retry/backoff, dead-letter handling, and reconciliation summary skeletons.

## Implemented platform scope (V1-V3 baseline)

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
- Identity lifecycle foundation endpoints for invite issuance/acceptance, verification requests/completions, and password-reset requests/completions.
- Refresh token issuance on register/login.
- Refresh token rotation on `POST /api/v1/auth/refresh`.
- Session inventory endpoint at `GET /api/v1/auth/sessions`.
- Session revoke-all endpoint at `POST /api/v1/auth/sessions/revoke-all`.
- MFA enrollment and verification endpoints for authenticated users.
- MFA step-up endpoint for admin-sensitive actions when MFA is enrolled.
- Refresh token revocation via:
  - `POST /api/v1/auth/logout`
  - `POST /api/v1/auth/revoke`
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
- Dedicated tenant audit log endpoint at `GET /api/v1/tenant/audit-logs`.
- Dedicated tenant usage analytics foundation endpoint at `GET /api/v1/tenant/analytics/usage` (tenant-scoped aggregation from audit events).

### Plans and subscription management

- Public plan catalog endpoint at `GET /api/v1/plans`.
- RBAC-protected plan upgrade endpoint at `POST /api/v1/plans/upgrade`.
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
- tenant suspension enforcement and tenant-resolution precedence safeguards
- tenant-scoped audit log retrieval
- tenant admin user-management behavior
- rate-limit header behavior plus explicit rate-limit rejection responses
- RBAC permission evaluation and authorization handler behavior
- internal billing callback validation (including contract-version rejection), lifecycle handling, cross-tenant rejection, and idempotency/replay protection
- P0 cross-service contract conformance coverage for BillingService -> .NET callback behavior (valid signed payloads, required-field failures, invalid version/signature, tenant/subscription mismatch, and duplicate-event idempotency expectations)
- BillingService callback producer contract coverage to assert payload schema/version plus `providerEventId` fallback-to-`eventId` behavior when provider ids are absent
- deeper security-focused scenarios for authentication negatives, authorization denials, tenant-isolation tampering, input validation abuse cases, and internal billing signature hardening
- identity-hardening edge cases for verification/password-reset token replay resistance and MFA step-up purpose binding on admin-sensitive actions
- tenant billing visibility and self-service action behavior, including tenant-scoped subscription/invoice reads, cancel/reactivate state transitions, and clean invalid-state error handling
- entitlement matrix harness regression coverage for billing/admin/analytics endpoint-gated entitlement keys across lower-plan/no-add-on negatives, add-on grants, override allow/deny precedence, and representative non-active subscription lifecycle combinations (allow + deny paths)
- entitlement boundary security checks covering unauthorized access and tenant-scoped entitlement isolation on gated billing surfaces

## V3 completion summary

V3 is now closed as complete. The repository should no longer be interpreted as "V3 planned next" or "under active V3 development."

### What V3 completed

- productionized internal billing callback lifecycle handling in the .NET API with authenticated contract validation and idempotent application semantics
- productionized BillingService workflow durability foundations including persistent queue/retry/dead-letter state and replay-safe deduplication
- added tenant-facing billing self-service foundations against internal subscription state (`status`, `invoices`, `cancel`, `reactivate`)
- delivered entitlements model foundations with progressive enforcement on selected billing/admin/analytics surfaces
- delivered identity/security hardening slices (invite/verification/reset lifecycle foundations, session inventory/revoke-all, MFA enrollment + step-up)
- delivered usage analytics foundation endpoints and initial outbound webhook delivery foundation with signed payload semantics
- expanded docs/runbooks and automated coverage around tenant safety, replay/idempotency handling, and operational diagnostics

### Implemented vs design-only status

**Implemented in-repo (V3 complete):**

- BillingService durable workflow runtime primitives and reconciliation scaffolding
- .NET internal billing callback ingestion, signature checks, contract versioning, and event-id inbox protection
- tenant billing self-service foundation endpoints on internal billing state
- entitlement schema/seeding/evaluator/enforcer baseline with progressive rollout
- identity lifecycle hardening + MFA step-up baseline
- usage analytics foundation + outbound webhook delivery foundation

**Design-only / post-V3 planning artifacts (not implemented as runtime behavior):**

- `docs/V3-Observability-and-Operations-Design.md` remains a design artifact for future exporter/dashboard/alert maturation
- any future provider-expansion work beyond the currently implemented provider-connected scope should be tracked as post-V3 roadmap work, not open V3 scope

## Operational notes (durable workflow iteration)

The current durable workflow iteration adds operational primitives in `BillingService` that are designed to survive restarts and reduce duplicate processing risk while live provider integration is still pending:

- file-backed workflow state persistence for queued work, retry metadata, and dead-lettered events
- replay-safe deduplication keyed by normalized `eventId`
- retry with bounded exponential backoff and max-attempt dead-lettering
- reconciliation comparison job scaffolding that can detect provider/internal drift once live readers are configured

These capabilities improve service resilience, but they are still **pre-live** because webhook verification, provider SDK calls, and authenticated callback delivery to the .NET API are not yet wired end-to-end.

For operational procedures (startup checks, state-file hygiene, replay handling, dead-letter triage, and reconciliation troubleshooting), use:

- `docs/Billing-Workflow-Runbook.md`
- `docs/Entitlements-Model.md`
- `docs/V4-Entitlement-Matrix-Test-Design.md`
- `docs/Identity-and-Security.md`
- `docs/Usage-Analytics.md`
- `docs/Outbound-Webhooks.md`
- `docs/V3-Observability-and-Operations-Design.md` (design-only plan for exporters, dashboards, and alerts)

## API surface summary

### Public/auth endpoints

- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/revoke` (authenticated + RBAC-protected)
- `GET /api/v1/auth/sessions` (authenticated)
- `POST /api/v1/auth/sessions/revoke-all` (authenticated)
- `POST /api/v1/auth/invites` (authenticated + RBAC-protected)
- `POST /api/v1/auth/invites/accept`
- `POST /api/v1/auth/verification/request`
- `POST /api/v1/auth/verification/complete`
- `POST /api/v1/auth/password-reset/request`
- `POST /api/v1/auth/password-reset/complete`
- `POST /api/v1/auth/mfa/enroll/initiate` (authenticated)
- `POST /api/v1/auth/mfa/enroll/verify` (authenticated)
- `POST /api/v1/auth/mfa/step-up` (authenticated)
- `GET /api/v1/plans`
- `GET /health`
- `GET /metrics`

### Tenant/admin endpoints

Under `api/admin/tenant`:

- `GET /api/v1/admin/tenant`
- `GET /api/v1/admin/tenant/users`
- `POST /api/v1/admin/tenant/users`
- `PUT /api/v1/admin/tenant/users/{userId}/role`
- `DELETE /api/v1/admin/tenant/users/{userId}`
- `GET /api/v1/admin/tenant/audit-logs`

Additional tenant-scoped endpoint:

- `GET /api/v1/tenant/audit-logs`
- `GET /api/v1/tenant/analytics/usage`
- `GET /api/v1/billing/status`
- `GET /api/v1/billing/invoices` (foundation feed sourced from tenant-scoped internal invoice billing events)
- `POST /api/v1/billing/subscription/cancel`
- `POST /api/v1/billing/subscription/reactivate`

### Internal service endpoint

- `POST /api/internal/billing/subscription-events`

### Outbound tenant webhooks (foundation)

The .NET API includes a first outbound webhook infrastructure slice for tenant events:

- versioned envelope contract (`2026-04-13`) containing `eventId`, `tenantId`, `eventType`, `correlationId`, and `occurredAtUtc`
- endpoint-specific HMAC-SHA256 request signing (`X-Tenant-Webhook-Signature`) with timestamp and delivery id binding
- persisted delivery state with retry scheduling and terminal status tracking
- replay/idempotency support via `SourceEventKey` dedupe at publish time and stable `X-Tenant-Webhook-Idempotency-Key` per delivery

See `docs/Outbound-Webhook-Contract.md` for contract and verification details.
Implementation and rollout notes for this iteration are in `docs/Outbound-Webhooks.md`.

### Tenant usage analytics (foundation)

The .NET API includes a tenant-safe usage analytics foundation sourced from tenant-scoped audit events:

- endpoint: `GET /api/v1/tenant/analytics/usage`
- bounded lookback window using `days` query clamping for safe query cost
- optional action filtering and top-action aggregation to support product and operations reads
- RBAC + tenant-context enforcement aligned with existing tenant/admin safeguards

Implementation and follow-up notes for this iteration are in `docs/Usage-Analytics.md`.

## Local deterministic orchestration profile (P0)

For a repeatable local V4 flow, use the repo-level scripts under `scripts/local/`.

### Scripted sequence (exact order)

From repository root:

```bash
scripts/local/bootstrap.sh
scripts/local/seed.sh
scripts/local/run.sh
# in a second shell while run.sh is active:
scripts/local/smoke.sh
scripts/local/test.sh
```

What each script does:

- `bootstrap.sh`
  - runs `dotnet restore`
  - runs `dotnet build --no-restore`
  - applies EF migrations using `/tmp/dotnet-tools/dotnet-ef` when available
  - runs `npm ci` in `BillingService`
- `reset.sh`
  - removes local logs and BillingService durable workflow state file
  - drops and recreates the local DB through `dotnet-ef` when available
  - prints explicit manual DB reset commands when `dotnet-ef` is unavailable
- `seed.sh`
  - reapplies EF migrations to keep model-managed seed data current
  - explicitly documents the remaining manual boundary for scenario/demo tenant seeding
- `run.sh`
  - starts the API on `http://localhost:5000`
  - starts BillingService on `http://localhost:3001`
  - writes logs to `.local-api.log` and `.local-billing.log`
- `smoke.sh`
  - verifies `GET /health` on API and BillingService
  - verifies BillingService accepts `POST /webhooks/provider`
- `test.sh`
  - runs the full required .NET validation sequence
  - runs BillingService install/build/test checks

Expected behavior:

- `bootstrap.sh` exits non-zero if restore/build/install fail.
- `run.sh` keeps both services running until `Ctrl+C`, then stops both.
- `smoke.sh` exits non-zero if any health/webhook check fails.
- `test.sh` exits non-zero if any .NET or BillingService validation command fails.

Typical local runtime expectations on a warm machine (non-binding, hardware-dependent):

- `bootstrap.sh`: ~2-6 minutes (restore/build/install work dominates)
- `reset.sh`: ~30-90 seconds (tooling + DB size dependent)
- `seed.sh`: ~10-30 seconds when migrations are already current
- `smoke.sh`: under 10 seconds after both services are healthy
- `test.sh`: ~3-10 minutes depending on test cache/warmth

### Runtime and validation order (failure-triage reference)

When troubleshooting, keep this exact execution order so failures are isolated to one stage:

1. `scripts/local/bootstrap.sh` (dependency restore/build/install + migration apply when tool exists)
2. `scripts/local/seed.sh` (migration/seed refresh + manual seed boundary reminder)
3. `scripts/local/run.sh` (start API and BillingService)
4. `scripts/local/smoke.sh` in a second shell (runtime health + placeholder webhook acceptance)
5. `scripts/local/test.sh` (full validation sequence for .NET + BillingService)

If a step fails, fix that step first and rerun it before continuing to later steps.

### Local failure triage (repo-specific)

| Failure type | Where it fails | Immediate next steps |
| --- | --- | --- |
| Dependency install failure | `bootstrap.sh` or `test.sh` during `dotnet restore` / `npm ci` | Re-run the failing command directly to surface full output (`dotnet restore` from repo root or `cd BillingService && npm ci`). Confirm SDK/runtime prerequisites from this README are installed locally and retry bootstrap/test after the direct command succeeds. |
| Migration/tooling failure | `bootstrap.sh`, `reset.sh`, or `seed.sh` at `/tmp/dotnet-tools/dotnet-ef` steps | If the tool is missing, run the printed manual command: `dotnet ef database update --project Infrastructure --startup-project Presentation` (or reset pair from `reset.sh`). If the tool exists but fails, validate DB connection secrets in `Presentation` and retry the exact EF command shown in script output. |
| Service start failure | `run.sh` exits quickly or one PID terminates | Inspect `.local-api.log` and `.local-billing.log` immediately. Fix the first startup error in the corresponding service, then rerun `scripts/local/run.sh` before any smoke/test step. |
| Port/config mismatch | `run.sh` can start but `smoke.sh` health checks fail or hit wrong endpoints | Ensure `API_URL` and `BILLING_URL` values match between `run.sh` and `smoke.sh` invocations. If overriding ports, export both variables in each shell before running scripts. |
| Smoke-test failure | `smoke.sh` fails `GET /health` or webhook POST | Confirm `run.sh` is still active, then check `.local-api.log` and `.local-billing.log` for readiness/startup exceptions. Re-run `smoke.sh` only after both services are healthy and listening on expected URLs. |
| Test-suite failure | `test.sh` fails any numbered step | Use the step number printed by `test.sh` to rerun only the failing command directly (`dotnet test --no-build --verbosity normal`, `npm run build`, or `npm test`). Fix forward at that layer, then rerun full `scripts/local/test.sh` to ensure sequence-level pass. |

Manual-only remainder (current P0 scope):

- This smoke path validates service readiness and placeholder webhook acceptance only.
- End-to-end provider webhook verification and authenticated BillingService -> .NET callback flow remain manual/post-P0 because live provider wiring is still pre-live.

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

Identity hardening iteration notes:

- No additional mandatory runtime secrets were introduced for invites, verification/reset tokens, MFA enrollment, or step-up sessions in this iteration.
- Existing JWT settings (`Jwt:Secret`, issuer/audience, expiration) remain required and security-sensitive.
- `IIdentityNotificationService` is still a logging placeholder and does **not** require provider credentials yet. When a live mail provider is wired, add the provider API key/secret in user-secrets or environment configuration (do not hardcode).
- For identity/security behavior and current follow-up items, see `docs/Identity-and-Security.md`.

Optional verification:

```bash
dotnet user-secrets list
```

### Apply database migrations

From repository root:

```bash
dotnet ef database update --project Infrastructure --startup-project Presentation
```

Entitlements iteration notes:

- The entitlements model and rollout contract are documented in `docs/Entitlements-Model.md`.
- Entitlements require the latest migration chain that includes:
  - `20260409090000_AddEntitlementsFoundation`
  - `20260409185041_AddProgressiveEntitlementGates`
- Re-run database update after pulling changes to ensure seeded entitlement keys/plan mappings are present before local runs/tests.

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
dotnet --info
/tmp/dotnet-tools/dotnet-ef --version
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
```

BillingService validation commands (required only when BillingService code changes in the iteration):

```bash
cd BillingService
npm run build
npm test
```

Or run the deterministic local profile sequence:

```bash
scripts/local/bootstrap.sh
scripts/local/run.sh
scripts/local/smoke.sh
```

## Documentation status for this completed iteration

Documentation was reviewed for accuracy against the current implemented baseline.

- `README.md` now reflects the completed April 20, 2026 P1.1 entitlement matrix regression iteration and its explicit implemented scope.
- `docs/V4-Implementation-Backlog.md` now records P1.1 as foundation + first regression expansion complete, including documented boundaries and follow-up scope.
- `docs/Entitlements-Model.md` now documents implemented matrix regression coverage for evaluator precedence, endpoint-gated entitlement keys, and representative lifecycle combinations.
- `docs/V4-Entitlement-Matrix-Test-Design.md` was added to capture the implemented harness structure, covered scenarios, and intentional scope boundaries.
- `docs/Internal-Billing-Contract.md` and `BillingService/README.md` remain accurate because no billing contract or BillingService runtime behavior changed in this iteration.

## Additional docs

- `BillingService/README.md`
- `docs/V3-Implementation-Backlog.md`
- `docs/V4-Implementation-Backlog.md`
- `docs/Internal-Billing-Contract.md`
- `docs/Billing-Workflow-Runbook.md`
- `docs/Entitlements-Model.md`
- `docs/V4-Entitlement-Matrix-Test-Design.md`
