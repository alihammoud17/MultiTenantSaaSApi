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

3. **Billing event fixture pack + replay tests**
   - create fixture-driven tests covering duplicates, out-of-order sequences, stale timestamps, and invalid signatures.

4. **Tenant isolation invariant suite**
   - add targeted negative-path integration tests that assert “no cross-tenant data access” across admin, analytics, billing, and webhook-related surfaces.

5. **V4 documentation baseline**
   - update README + docs to include a clear “pre-deployment capability map,” what is demoable today, and what remains post-deployment.

## P1 (next)

1. **Entitlement matrix test harness**
   - codify plan/add-on/override permutations and expected gate outcomes to reduce regression risk.

2. **Developer workflow hardening**
   - standardize local scripts for bootstrap/reset/seed/smoke/test and document expected run times and failure triage steps.

3. **Replay-safe outbound webhook verification tests**
   - extend delivery tests for retry jitter windows, duplicate publish suppression, and terminal-failure observability fields.

4. **Local observability quality gates**
   - add tests/assertions for required structured log fields and correlation-id propagation across critical request paths.

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

## 5) Docs/README updates needed before starting V4

Update these first, before implementation begins:

1. `README.md`
   - add a “V4 pre-deployment direction” section
   - clearly split code-first maturity vs deployment maturity
   - add a quick local “full platform smoke path” section

2. `docs/V3-Implementation-Backlog.md`
   - add a pointer to this V4 backlog as the active post-V3 execution track

3. `docs/V4-Implementation-Backlog.md` (this file)
   - keep backlog status and priorities current as slices are completed

4. `docs/Internal-Billing-Contract.md`
   - no immediate contract change required, but add/update only when V4 contract tests expose ambiguities

5. `BillingService/README.md`
   - update only when V4 changes BillingService runtime behavior, scripts, or environment configuration

---

## Non-goals for V4 (pre-deployment)

- claiming production-readiness based solely on local tests
- introducing live-ops obligations that cannot be validated locally
- coupling roadmap success to unavailable infrastructure
