# V4 Implementation Backlog (Code-First / Pre-Deployment)

## Purpose

This V4 plan is optimized for a **non-deployed GitHub project** where implementation quality, local testability, and architecture maturity are prioritized ahead of live production infrastructure.

Date of planning baseline: **April 13, 2026**.

## 1) Current-state summary

### Implemented platform capabilities (code-first maturity)

The repository already includes a strong multi-tenant SaaS baseline across .NET API and BillingService:

- tenant registration, authentication, refresh-token lifecycle, and MFA/step-up foundations
- tenant-context enforcement, RBAC permissions/policies, and tenant-scoped audit logs
- subscription lifecycle state and plan upgrade flow in .NET
- internal billing callback contract handling with signature validation, contract version checks, and idempotency inbox semantics
- BillingService durable workflow baseline with persistent queue/retry/dead-letter state and reconciliation scaffolding
- entitlements/add-ons foundations with progressive feature gating on selected billing/admin/analytics endpoints
- usage analytics foundation and outbound webhook delivery foundations
- broad integration/unit security coverage for tenant isolation, authorization, replay/idempotency, and identity-edge scenarios

### Local/demo readiness

Local/demo readiness is good for a **single-machine deterministic demo**:

- clear local prerequisites and startup instructions for API + BillingService
- migration and secrets setup guidance for development environments
- repository-level validation command sequence for .NET and BillingService test/build checks
- API surface is demoable through Swagger, tenant flows, billing-state simulations, entitlement gates, analytics, and internal-callback emulation

Current constraints (expected pre-deployment):

- live provider webhook verification and fully wired provider-to-.NET callback delivery are not end-to-end runtime complete
- production telemetry exporters/dashboards/alerts are still design-oriented rather than deployment-verified

### Developer experience (DX)

DX is already above average for a pre-deployment codebase:

- layered architecture with separation between Presentation/Application/Domain/Infrastructure
- explicit docs for billing contract, runbook, entitlements, security, and analytics
- significant automated test coverage in both .NET and BillingService

Largest DX gaps to close in V4 (without requiring deployment):

- deterministic local orchestration (one-command bootstrap + smoke path)
- stronger contract-test tooling between .NET and BillingService
- scenario-driven test fixtures for realistic multi-tenant and billing timelines
- contribution guidance focused on code-first validation expectations

### Enterprise-ready architecture (code-first vs deployment maturity)

#### Code-first enterprise maturity (already present)

- explicit ownership boundaries (.NET as system of record; provider logic isolated in BillingService)
- idempotency/replay protections on internal billing event handling
- tenant-safety constraints visible in middleware, endpoints, and tests
- progressive security model (RBAC, invite/reset/verification lifecycle, MFA step-up)

#### Deployment/operations maturity (intentionally partial pre-deployment)

- no validated production SLO/error-budget practices yet
- no deployment-proven alerting/dashboards/exporter wiring yet
- no production-hardened key rotation/secrets operations workflow validation yet

This split is expected and appropriate for a non-deployed stage.

### Features that can be meaningfully built + tested before deployment

High-value pre-deployment features include:

1. local-first end-to-end billing simulation harness (provider event fixtures -> BillingService normalization -> signed .NET callback)
2. contract conformance tests for .NET <-> BillingService callback schemas and signature expectations
3. entitlement regression matrix tooling (plan/add-on/override combinations)
4. tenant-safety fuzz/invariant test suite for cross-tenant leakage prevention
5. deterministic outbound webhook replay/ordering and retry test harness
6. developer bootstrap automation and smoke-test scripts for reproducible local demos

### Features that should wait until after first deployment

Postpone until live environments exist:

- real on-call rotations and pager policies
- SLOs based on production traffic/error patterns
- autoscaling and capacity tuning based on live load
- provider incident runbooks that depend on real provider failure telemetry
- cost governance tuning based on real infrastructure billing profiles

## 2) V4 north star recommendation

### North star

**Make the repository the best possible “production-like local platform”: contract-safe, tenant-safe, and test-heavy, with deterministic workflows that prove behavior before the first deployment.**

### V4 success criteria

V4 should be considered successful when:

- a developer can bootstrap both services and run a full local scenario with one documented workflow
- .NET <-> BillingService contracts are verified via automated contract tests and fixture packs
- tenant isolation + entitlement behavior are provable through matrix/invariant tests, not only happy paths
- docs clearly separate “code-ready” from “deployment-ready” so roadmap expectations remain realistic

## 3) Backlog proposal (optimized for non-deployed execution)

## P0 (build now)

1. **Local deterministic orchestration profile** *(Completed April 15, 2026)*
   - added repo-level local orchestration scripts in `scripts/local/` for bootstrap/run/smoke
   - documented exact command order and expected behavior in `README.md`, `BillingService/README.md`, and `docs/Local-Orchestration-Profile.md`
   - current smoke scope validates local API/BillingService health and placeholder webhook acceptance; full provider->callback E2E remains manual until live wiring slices land
   - explicitly split code-ready local validation claims from production-readiness claims in repo-level docs

2. **Cross-service contract test suite** *(Completed April 18, 2026)*
   - implemented focused automated contract tests in `.NET` integration coverage (`Tests/Integration/CrossServiceBillingContractTests.cs`) for:
     - valid signed callback acceptance
     - missing required fields rejection
     - unsupported contract version rejection
     - invalid signature rejection
     - tenant/subscription mismatch rejection
     - duplicate `eventId` idempotent handling
   - implemented BillingService producer contract tests in `BillingService/tests/billingCallbackContract.test.ts` to verify callback payload shape/version and provider-event fallback mapping.
   - the P0 suite remains deterministic and local (test harness signatures and file-backed workflow state only; no live provider dependency).
   - docs follow-up from test findings:
     - clarified required-field rejection and duplicate response semantics in `docs/Internal-Billing-Contract.md`
     - clarified BillingService `providerEventId` fallback-to-`eventId` contract expectation in `docs/Internal-Billing-Contract.md` and `BillingService/README.md`

3. **Billing event fixture pack + replay tests** *(Completed April 19, 2026)*
   - added fixture-driven replay tests in `BillingService/tests/billingEventFixtureReplay.test.ts` with scenario pack data in `BillingService/tests/fixtures/billingEventFixturePack.ts`.
   - covered deterministic local scenarios for:
     - duplicate delivery replay safety (`eventId` dedup in durable queue)
     - out-of-order delivery handling (arrival-order processing with preserved normalized timestamps)
     - stale timestamp acceptance behavior in current pre-live normalization/workflow slice
     - invalid signature rejection before queue enqueue/processing
   - suite remains local-only and deterministic (no live provider dependencies; adapter responses are fixture-driven).

4. **Tenant isolation invariant suite** *(Completed April 19, 2026)*
   - added targeted negative-path integration tests in `.NET` integration coverage (`Tests/Integration/TenantIsolationSecurityTests.cs`) to assert no cross-tenant data access across:
     - admin management surfaces (token/header tenant mismatch and cross-tenant user-id mutation attempts)
     - analytics + audit surfaces (cross-tenant tenant-header tampering rejection)
     - billing read surfaces (`/api/v1/billing/status` and `/api/v1/billing/invoices`)
     - internal billing webhook/callback processing (tenant/subscription mismatch rejected with no state mutation and no inbox persistence)
   - hardened tenant resolution behavior for authenticated requests by rejecting JWT tenant vs request-tenant mismatch with `TenantMismatch` and preserving JWT tenant authority.

5. **V4 documentation baseline** *(Completed April 19, 2026)*
   - updated `README.md`, `BillingService/README.md`, `docs/V4-Implementation-Backlog.md`, `docs/Internal-Billing-Contract.md`, and `docs/V4-CrossService-Contract-Test-Design.md` to publish a consistent pre-deployment capability map.
   - documented explicit local-demoable behavior vs post-deployment remaining work for platform and BillingService scopes.
   - preserved design intent while removing stale “pre-start” documentation guidance that no longer matches completed P0 slices.

## P1 (active)

1. **Entitlement matrix test harness** *(P1.1 foundation + first regression expansion completed April 20, 2026)*
   - added a first reusable .NET unit-test harness slice with:
     - shared entitlement matrix case model
     - deterministic fixture builders for plan/add-on/override permutations
     - reusable assertion helpers for resolved value/source/allowance checks
   - initial matrix coverage now validates boolean precedence and integer add-on merge semantics (`Increment`) with override precedence.
   - first regression expansion slice completed April 20, 2026:
     - added representative endpoint-gate coverage for billing/admin/analytics entitlements (`feature.billing.*`, `feature.admin.*`, `feature.analytics.*`)
     - validated allow + deny outcomes across plan defaults, add-on grants, explicit overrides, and subscription lifecycle statuses (`Active`, `GracePeriod`, `Canceled`)
   - negative-path + precedence expansion completed April 20, 2026:
     - validated lower-plan/no-required-add-on denial, override-allow on lower defaults, and override-deny over higher plan grants.
     - validated current evaluator behavior for non-active subscription status interactions (including `Expired`) without asserting unsupported `Suspended` semantics.
     - documented boundary intersection expectation: tenant/authorization enforcement remains covered by integration security tests, while matrix tests continue to focus on evaluator precedence determinism.
   - remaining follow-up: broaden matrix dimensions (additional add-on merge modes and deeper endpoint-level scenario breadth) in subsequent P1 slices.

2. **Developer workflow hardening foundation** *(P1.2 completed April 25, 2026)*
   - standardized local script set under `scripts/local/` for:
     - `bootstrap.sh`
     - `reset.sh`
     - `seed.sh`
     - `smoke.sh`
     - `test.sh`
   - preserved the existing two-shell orchestration flow (`run.sh` + `smoke.sh`) while adding explicit wrappers for reset/seed/test.
   - documented expected runtime ranges and concrete failure-triage guidance in `README.md`, `BillingService/README.md`, and `docs/Local-Orchestration-Profile.md`.
   - extended triage documentation with explicit runtime/validation order and step-by-step paths for dependency, tooling/migration, service startup, port/config alignment, smoke, and test failures.
   - made the current manual boundary explicit: scenario/demo tenant seed packs are still manual in this iteration.

3. **Top-level developer-loop command index** *(P1.3 completed April 25, 2026)*
   - added `scripts/dev.sh` as a thin top-level command index/dispatcher for common local flows:
     - `bootstrap`
     - `reset`
     - `seed`
     - `run`
     - `smoke`
     - `test`
   - kept `scripts/local/*.sh` as first-class underlying scripts and delegated directly to them (no behavior replacement or hidden orchestration).
   - refreshed `README.md` and `docs/Local-Orchestration-Profile.md` examples to show the recommended day-to-day `scripts/dev.sh` loop while preserving direct-script usage guidance.

4. **Replay-safe outbound webhook verification tests** *(P1.4 completed April 25, 2026)*
   - added first reusable outbound delivery test harness primitives for:
     - seeded tenant endpoint + publish request setup
     - deterministic dispatch-attempt simulation (including retry timing by forced due-at windows)
     - request/header capture for idempotency and signature-contract inspection
     - persisted delivery-state inspection across pending/retry/succeeded/exhausted transitions
   - first deterministic scenarios now validate:
     - retry scheduling and subsequent success behavior for failed-then-recovered deliveries
     - terminal failure (`Exhausted`) observability fields after bounded retry attempts
     - duplicate publish suppression behavior via `SourceEventKey` replay dedupe (single event/delivery row for duplicate publish request)
     - delivery metadata preservation across retries and recovery (`LastError`, status transitions, stable delivery/idempotency headers, contract-version header, and per-attempt timestamp capture)
     - currently implemented envelope-level correlation continuity assertions (`correlationId`, `eventId`, `tenantId`) across retries
   - automated guarantees now explicitly covered in deterministic tests:
     - replay/dedup: duplicate publish requests do not create duplicate event or delivery records
     - retry/recovery: transient failures schedule retries and recover to `Succeeded` with preserved delivery identity
     - retry exhaustion: bounded repeated failures end in deterministic `Exhausted` state with inspectable diagnostics
     - header/payload continuity: delivery + idempotency header continuity across attempts and envelope correlation continuity across retries
   - explicit current observability boundary documented by tests:
     - no dedicated outbound tracing header is currently emitted
     - retry visibility is exposed through delivery status/timestamps/status-code/error fields (no standalone retry history table yet)
   - remaining follow-up: broaden matrix depth for additional retry-window boundaries and multi-endpoint fan-out slices in later P1 iterations.

5. **Local observability contract definition (P1.5 completed April 26, 2026)**
   - added `docs/V4-Local-Observability-Contract.md` defining the current-stage local minimum observability guarantees without inventing unimplemented features.
   - contract now concretely defines:
     - `/health` and `/metrics` local availability expectations for both services
     - required structured diagnostic fields on current critical request/workflow paths
     - correlation continuity guarantees and current trace continuity boundary (only where implemented)
     - local failure-state diagnosability expectations
     - explicit safe-field requirements and explicit forbidden sensitive-data classes
   - contract explicitly forbids logging/asserting raw auth secrets/tokens and uncontrolled full payload dumps.
   - contract records current implementation gaps (log-schema quality-gate tests not yet implemented; BillingService trace-id derivation boundary).

6. **Health/metrics stability checks (first P1.5 slice completed April 26, 2026)**
   - added deterministic automated health/metrics stability checks for both services:
     - .NET API: `GET /health` and `GET /metrics` integration assertions for HTTP 200, JSON content type, required top-level shape, and basic sensitive-field absence checks
     - BillingService: `GET /health` and `GET /metrics` tests assert required shape/availability fields and basic sensitive-field absence checks
   - expanded local smoke gate (`scripts/local/smoke.sh`) to fail fast when either service `/metrics` endpoint is unreachable, non-JSON, or returns invalid JSON.
   - kept assertions intentionally narrow to avoid brittle coupling to incidental counter values while still verifying local reachability and payload stability for current repo usage.

7. **Correlation + structured-field quality gates (main P1.5 slice completed April 26, 2026)**
   - added deterministic tests that enforce representative correlation continuity and safe structured diagnostic fields across critical local debugging flows:
     - .NET internal billing callback path now has explicit correlation-header continuity assertions (`X-Correlation-ID`) and callback-correlation persistence checks (`BillingEventInbox.CorrelationId`).
     - .NET outbound webhook retry/exhaustion harness coverage now acts as a structured diagnostics gate for persisted delivery state (`AttemptCount`, `LastAttemptAtUtc`, `NextAttemptAtUtc`, `LastResponseStatusCode`, `LastError`, status transitions) plus envelope-level correlation/event/tenant continuity across retries.
     - BillingService request observability tests now enforce safe structured field presence on request lifecycle logs (`http.request.started` / `http.request.completed`) and webhook correlation echo behavior.
     - BillingService workflow processing tests now enforce dead-letter diagnostic field presence (`eventId`, `correlationId`, `tenantId`, `status`, `attempts`, `message`, `timestamp`) without asserting sensitive/raw payload material.
   - smallest safe observability fixes included:
     - BillingService workflow and enqueue/duplicate logs now include `correlationId` and explicit transition `status` fields where missing.
     - BillingService failure/rejection diagnostic messages are now sanitized before logging/persistence/response shaping to prevent token/secret/header leakage while retaining actionable failure context.
     - .NET billing callback duplicate-path log now includes callback `CorrelationId`.
     - .NET outbound webhook dispatcher now emits structured success/retry/transport-failure diagnostic logs with correlation-safe identifiers and transition context.
   - targeted failure-path quality-gate tests now also cover representative negative paths where observability commonly regresses:
     - transient workflow failure with retry scheduling diagnostics + sensitive-value absence assertions
     - terminal retry-exhaustion dead-letter diagnostics + persisted-state sensitive-value absence assertions
     - webhook invalid-signature/malformed rejection reason sanitization assertions
   - remaining follow-up: expand this representative gate set to additional sensitive request/error paths and introduce broader forbidden-field regression checks for logs/state.

8. **P1.5 finalization + verification pass (completed April 26, 2026)**
   - re-validated the required .NET command set (`dotnet --info`, `dotnet-ef --version`, `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build --verbosity normal`) during the P1.5 closeout pass.
   - re-validated BillingService test execution with `npm test`; current environment policy blocked fresh dependency restore via `npm ci` (403), so `npm run build` could not be re-run in this environment because `tsc` was unavailable without install.
   - documentation is now aligned to explicitly separate:
     - locally enforced observability guarantees now covered by automated gates
     - partially implemented observability surfaces that still need broader coverage
     - production telemetry/exporter/dashboard/alert work that remains post-P1.5 scope
   - **P1.5 completion decision:** complete for V4 local-quality-gate scope, with remaining follow-up tracked for P2+ and post-deployment tracks.

9. **AuthController application-layer boundary design (completed April 26, 2026, design-only)**
   - produced the pre-implementation boundary design for moving `AuthController` register/login/refresh orchestration behind application service interfaces without moving HTTP concerns into the application layer.
   - defined smallest-safe proposed `IAuthOrchestrationService` method surface, controller/service/infrastructure responsibility split, and typed result mapping strategy to preserve current response contracts where practical.
   - documented exact first-change file sequence and phased migration strategy to incrementally remove direct `ApplicationDbContext` dependency from targeted auth flows while preserving tenant, audit, refresh-token, MFA, and identity-lifecycle integrations.
   - implementation intentionally deferred to a follow-up thin vertical slice.

## P2 (later pre-deployment improvements)

1. **Reference demo tenant packs**
   - seeded datasets and scripted flows for “new tenant”, “growth tenant”, and “billing-failure tenant” scenarios.

2. **Architecture decision record (ADR) set for V4**
   - lightweight ADRs documenting contract versioning, idempotency strategy, and data ownership boundaries.

3. **Pre-deployment release checklist automation**
   - CI-level checks that enforce required docs, migration validity, and minimum scenario coverage before merge.

## 4) Items to postpone until after first deployment

Treat these as post-deployment tracks (not V4 blockers):

- production SLO/SLI thresholds based on real traffic
- paging/escalation policy tuning from real incident history
- autoscaling and performance tuning from actual workload traces
- provider cost/performance optimization from production billing usage
- full disaster-recovery game days against live infrastructure dependencies

## 5) Documentation baseline status (updated April 20, 2026)

The initial V4 documentation baseline is now in place.

Completed baseline updates:

1. `README.md`
   - includes a platform-level pre-deployment capability map
   - states demoable local scope vs post-deployment scope explicitly

2. `BillingService/README.md`
   - includes BillingService-specific capability map and local demo scope
   - distinguishes implemented local behaviors from post-deployment provider/runtime work

3. `docs/V4-Implementation-Backlog.md` (this file)
   - tracks P0 slices 1-5 as completed with concrete completion dates

4. `docs/Internal-Billing-Contract.md`
   - retains normative callback contract details and now documents how the contract is validated locally pre-deployment vs what still requires deployment verification

5. `docs/V4-CrossService-Contract-Test-Design.md`
   - updated from design-only posture to implemented baseline summary plus remaining follow-up matrix

6. P1.1 entitlement matrix harness documentation refresh
   - updated `README.md`, `docs/V4-Implementation-Backlog.md`, and `docs/Entitlements-Model.md` to explicitly capture implemented regression coverage for billing/admin/analytics entitlement keys
   - documented matrix scope boundaries (evaluator precedence determinism) vs integration-suite responsibilities (tenant/auth isolation enforcement)

Ongoing documentation expectations for V4:

- keep capability-map status current after every completed slice
- keep demoable-local claims strictly aligned to automated tests and local scripts
- keep post-deployment claims explicit and non-ambiguous to avoid overstating readiness

---

## Non-goals for V4 (pre-deployment)

- claiming production-readiness based solely on local tests
- introducing live-ops obligations that cannot be validated locally
- coupling roadmap success to unavailable infrastructure
