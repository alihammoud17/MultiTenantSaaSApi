# Deployment Readiness Backlog

## Goal

Define the **minimum remaining work** required to move from the current V4 **code-ready, pre-deployment local platform** to a **safe first deployment environment**, without changing architecture ownership boundaries or overstating production readiness.

## Current state summary

Grounded in current repository documentation:

- Deterministic local orchestration exists (`bootstrap/reset/seed/run/smoke/test`) and is explicitly documented as **code-ready local validation**, not production certification.
- Cross-service .NET API <-> BillingService callback contract tests and replay/idempotency fixture validation are implemented and treated as local pre-deployment quality gates.
- Tenant-isolation, authorization, auth hardening, and entitlement regression coverage are implemented for key sensitive surfaces.
- Outbound webhook foundation and tenant endpoint management exist, including pending signing-secret rotation issuance, but active cutover/finalization operations are deferred.
- Observability in current scope is local-grade: health/metrics endpoints, structured logs, and representative observability quality gates exist; deployment-proven exporters/alerts/SLO operations are explicitly not claimed yet.
- BillingService remains intentionally **pre-live** for provider-facing runtime: real provider webhook authenticity verification and fully wired authenticated callback flow are documented as still pending for deployed environments.

Explicitly **not** claimed as production-ready in current docs:

- Live provider webhook authenticity and provider -> BillingService -> .NET callback chain validation in deployed runtime.
- Deployment-proven telemetry backend/exporters, dashboard/alert operations, and on-call SLO/error-budget practice.
- Deployment-runbook-backed key rotation, incident automation, and DR/restore operational maturity.

## Deployment target (fill before execution)

> These fields must be filled and approved before executing any deployment-readiness implementation slices.

- **First deployment type:** `[TBD: single environment | staged envs | internal-only | customer-facing pilot]`
- **.NET API host:** `[TBD]`
- **BillingService host:** `[TBD]`
- **Database host:** `[TBD]`
- **Redis host:** `[TBD]`
- **Object/file storage needed:** `[TBD: yes/no + provider + purpose]`
- **Environment count for first rollout:** `[TBD]`
- **Browser client in scope:** `[TBD: yes/no]`
- **Real billing provider required before first deploy:** `[TBD: yes/no + rationale]`
- **Public DNS / HTTPS domain model:** `[TBD]`
- **Secret store approach:** `[TBD]`

## Planning principles

- Prioritize **true first-deploy blockers** ahead of polish.
- Keep each change a thin vertical slice with explicit blast-radius control.
- Preserve existing HTTP contracts and versioning rules unless intentionally changed.
- Preserve tenant isolation, RBAC boundaries, and .NET system-of-record ownership.
- Prefer explicit, reviewable runbooks/checklists over implicit tribal knowledge.
- Separate first-deploy blockers (P0) from immediate hardening (P1) and longer-term maturity (P2).
- Do not mark planning items complete until code, validation, and docs all land.

## Priority summary table

| Item | Priority | Track | Depends on | Notes |
| --- | --- | --- | --- | --- |
| Choose first deployment topology and environment model | P0 | Platform rollout model | Deployment target fields | Foundational decision that gates all other rollout steps. |
| Secrets and configuration inventory | P0 | Security/ops baseline | Topology decision | Must enumerate current env vars/settings per service and assign source-of-truth store. |
| Database migration execution and rollback plan | P0 | Data operations | Topology + secrets | Must move from local EF workflow to environment-safe migration runbook. |
| Billing scope decision for first deploy (live provider required?) | P0 | Billing rollout scope | Topology decision | Determines whether provider webhook/callback wiring is a pre-deploy blocker. |
| Provider webhook verification + callback delivery wiring (conditional) | P0 | Billing integration hardening | Billing scope decision | Mandatory before deployment if first environment requires live billing/provider flows. |
| Deployment smoke checklist | P0 | Runtime verification | Topology + secrets + migrations | Codifies post-deploy minimum health checks beyond local-only orchestration smoke. |
| Rollback checklist | P0 | Release safety | Deployment smoke checklist | Defines safe revert path for API, BillingService, schema, and config changes. |
| Tighten CORS from permissive baseline (if browser client in scope) | P1 | API security hardening | Browser scope decision | Current explicit permissive CORS is documented as temporary for pre-deployment. |
| Production telemetry/exporter path | P1 | Observability operations | Topology decision | Current observability contract is local-grade; export pipeline/alerts are pending. |
| Key rotation operational process | P1 | Security operations | Secrets inventory | Must define repeatable procedures for JWT/billing/webhook signing materials. |
| Environment promotion and release tagging workflow | P1 | Release/versioning ops | Topology + env count | Aligns with existing SemVer + tag-based deployment strategy. |
| DR / backup / restore drills | P2 | Reliability maturity | Stable first deployment | Post-first-deploy operational maturity and evidence-building. |
| Incident response automation / alert tuning | P2 | Operations maturity | Telemetry exporter path | Tune based on first environment signal quality and failure modes. |
| SLO / error-budget operation | P2 | Reliability governance | Incident + telemetry baseline | Should be based on real traffic/error characteristics. |

## P0 — First deployment blockers

### 1) Choose first deployment topology and environment model
- **Problem:** Current docs describe deterministic local orchestration, but not a committed first environment topology/model.
- **Why it blocks deployment or why it matters:** Hosting, network boundaries, DNS/TLS, env count, and rollout sequence cannot be finalized without this decision.
- **Smallest shippable slice:** Record an approved first-deploy target profile (hosts, network exposure, env count, rollout type) in deployment docs/runbook.
- **Validation:** Architecture/ops review confirms the profile is internally consistent and maps to existing system boundaries.
- **Done when:** Deployment target section is fully populated, reviewed, and treated as baseline input for follow-on P0 items.
- **Owner/system:** Platform architecture + API/Billing service owners.

### 2) Secrets and configuration inventory
- **Problem:** Current repo documents many env/config values, but no deployment-bound inventory with ownership, source, rotation class, and environment mapping.
- **Why it blocks deployment or why it matters:** First deployment cannot be safe without explicit secret ownership and non-repo secret injection paths.
- **Smallest shippable slice:** Create a single deployment config inventory mapping each required key for .NET API and BillingService to source, sensitivity class, and runtime injection method.
- **Validation:** Dry-run startup in target-like environment uses only mapped secret sources (no hardcoded/local-only fallback assumptions).
- **Done when:** Inventory exists, is reviewed by service owners, and is referenced by deployment runbook.
- **Owner/system:** Security/platform ops with API and BillingService maintainers.

### 3) Database migration execution and rollback plan
- **Problem:** EF migration operations are documented for local workflows; deployment-safe migration sequencing and rollback are not yet codified.
- **Why it blocks deployment or why it matters:** Schema changes without an explicit execute/verify/rollback plan are a release-risk blocker.
- **Smallest shippable slice:** Publish a first-deploy migration runbook covering prechecks, execution order, verification queries, rollback triggers, and ownership.
- **Validation:** Rehearse migration + rollback on a disposable target-like environment/database snapshot.
- **Done when:** Rehearsal evidence is recorded and migration/rollback checklist is linked from deployment procedures.
- **Owner/system:** .NET API + data operations owners.

### 4) Decide whether live billing provider integration is required before first deploy
- **Problem:** Current docs preserve pre-live BillingService boundaries; first deployment scope does not yet decide if live provider flows are required immediately.
- **Why it blocks deployment or why it matters:** This decision determines whether webhook authenticity/callback wiring is a hard pre-deploy gate or deferred rollout.
- **Smallest shippable slice:** Record an explicit go/no-go decision with rationale and constraints (internal pilot vs external billing-enabled rollout).
- **Validation:** Decision is reflected consistently across deployment target, backlog priorities, and release acceptance criteria.
- **Done when:** Billing scope is explicit and dependencies are re-prioritized accordingly.
- **Owner/system:** Product/platform leadership + BillingService owner.

### 5) Provider webhook verification and callback delivery wiring (conditional on billing scope)
- **Problem:** Live provider webhook authenticity verification and authenticated callback delivery to .NET are documented as partial/pre-live.
- **Why it blocks deployment or why it matters:** If live billing is in first-deploy scope, this is a direct safety and correctness blocker.
- **Smallest shippable slice:** Implement one provider path end-to-end for verified webhook ingestion, normalized event mapping, authenticated callback dispatch, and rejection/idempotency diagnostics.
- **Validation:** Deterministic integration tests + target-like environment smoke prove verified provider event -> BillingService -> .NET callback flow.
- **Done when:** End-to-end path is operationally runnable with runbook steps and failure/rollback handling documented.
- **Owner/system:** BillingService (provider-facing) + .NET API callback contract owner.

### 6) Deployment smoke checklist
- **Problem:** Existing smoke checks are local-orchestration-focused and intentionally limited to health/metrics/placeholder webhook acceptance.
- **Why it blocks deployment or why it matters:** First deployment needs a post-release minimum verification checklist for real environment readiness and contract safety.
- **Smallest shippable slice:** Define a concise deploy-time smoke checklist (API/Billing runtime health, critical auth route, tenant-safe read, billing callback/auth path as scoped).
- **Validation:** Execute checklist in a target-like environment and capture pass/fail evidence with correlation IDs.
- **Done when:** Checklist is versioned, repeatable, and required for deployment acceptance.
- **Owner/system:** Release owner + API/Billing service owners.

### 7) Rollback checklist
- **Problem:** Current docs distinguish pre-deployment maturity, but do not define an explicit first-environment rollback decision tree/checklist.
- **Why it blocks deployment or why it matters:** No safe first deployment without an agreed rollback path across app binaries, configs/secrets, and schema coupling.
- **Smallest shippable slice:** Publish rollback checklist with trigger criteria, command sequence, schema compatibility caveats, and communication steps.
- **Validation:** Tabletop rehearsal for at least one failed deployment scenario.
- **Done when:** Rollback checklist is approved and linked from deployment runbook and release checklist.
- **Owner/system:** Platform/release engineering + API/Billing owners.

## P1 — Immediate post-deploy or pre-public-use items

### 1) Tighten CORS from permissive baseline (if browser client is in scope)
- **Problem:** Current API CORS is explicit but intentionally permissive for local/pre-deployment use.
- **Why it blocks deployment or why it matters:** If browser clients are in scope, wildcard CORS is inappropriate for public exposure.
- **Smallest shippable slice:** Replace wildcard policy with explicit allowed origins/methods/headers for first client surface.
- **Validation:** Browser flow succeeds for approved origins and fails for unapproved origins.
- **Done when:** Production-target CORS policy is deployed and documented per environment.
- **Owner/system:** .NET API owner + frontend/platform owner.

### 2) Production telemetry/exporter path
- **Problem:** Current observability contract is local scope; production exporter/dashboards/alerts are not deployment-verified.
- **Why it blocks deployment or why it matters:** Post-deploy diagnosability and incident handling require durable telemetry sinks and actionable alerts.
- **Smallest shippable slice:** Wire one supported telemetry export path for both services plus baseline health/error alerts.
- **Validation:** Synthetic fault and normal traffic produce expected telemetry and alert behavior.
- **Done when:** Export path and minimal alert runbook are operational in first environment.
- **Owner/system:** Observability/platform ops + service owners.

### 3) Key rotation operational process
- **Problem:** Rotation primitives exist in parts of the system, but end-to-end operational process is not yet fully codified for deployment operations.
- **Why it blocks deployment or why it matters:** Safe operation requires repeatable key/secret rotation procedures and auditability.
- **Smallest shippable slice:** Document and rehearse rotation runbook for JWT secrets and billing/webhook callback signing materials used in first environment.
- **Validation:** Controlled rehearsal rotates keys without breaking signed request/response flows.
- **Done when:** Rotation SOP exists, is tested once, and linked from ops runbooks.
- **Owner/system:** Security/platform ops + API/Billing owners.

### 4) Environment promotion / release tagging workflow
- **Problem:** Versioning strategy defines SemVer + tag-based deployment, but first-environment promotion workflow is not yet operationalized as a release checklist.
- **Why it blocks deployment or why it matters:** Repeatable deployments require explicit build provenance and promotion rules.
- **Smallest shippable slice:** Define and document first release workflow from branch -> CI -> release tag -> deploy artifact.
- **Validation:** Run one dry-run release process producing immutable tagged artifact and deployment evidence.
- **Done when:** Promotion workflow is documented and used for first deployment candidate.
- **Owner/system:** Release engineering.

## P2 — Later production maturity

### 1) DR / backup / restore drills
- **Problem:** Backup/restore and disaster recovery outcomes are not yet validated in environment operations.
- **Why it blocks deployment or why it matters:** Critical for medium-term reliability and compliance confidence after initial rollout.
- **Smallest shippable slice:** Define backup policy and run first restore drill for database plus required service state.
- **Validation:** Timed restore drill succeeds and data integrity checks pass.
- **Done when:** Restore runbook and drill evidence are captured with follow-up gaps tracked.
- **Owner/system:** Platform ops + data owners.

### 2) Incident response automation / alert tuning
- **Problem:** Initial alerts are expected to need tuning once real failure/traffic patterns appear.
- **Why it blocks deployment or why it matters:** Reduces alert fatigue and MTTR after first environment stabilizes.
- **Smallest shippable slice:** Add top 3 incident automations and retune baseline alerts from first deployment incidents.
- **Validation:** Simulated incident exercises verify alert routing and runbook automation behavior.
- **Done when:** Alert noise is reduced to agreed baseline and key runbook steps are automated.
- **Owner/system:** Operations/on-call ownership.

### 3) SLO / error-budget operation
- **Problem:** No production traffic-derived SLO/error-budget practice is currently established.
- **Why it blocks deployment or why it matters:** Needed for sustainable reliability governance and release pacing post-launch.
- **Smallest shippable slice:** Define initial SLIs/SLOs for core API and billing callback workflows, then pilot one error-budget review cadence.
- **Validation:** One full reporting interval produces SLO reports and explicit release/risk decisions.
- **Done when:** SLO review loop is active with documented ownership and action policy.
- **Owner/system:** Engineering leadership + operations.

## Open decisions

- Is **real billing provider integration** mandatory before first deployment, or can first deploy be internal/pilot without live provider webhooks?
- Are **browser clients** in first-deploy scope (which would force immediate CORS hardening and client-domain planning)?
- What is the **first environment topology** (single env vs staged envs, internal-only vs public-facing)?
- Where will runtime **secrets** live (store/system and injection model), and who owns rotation authority?
- Is **one environment** sufficient for first rollout risk tolerance, or is at least one pre-prod promotion step required?

## Completion rule

No backlog item in this document is complete until:

1. implementation lands in code/config/runbooks,
2. validation checks pass with recorded evidence,
3. affected docs and operational runbooks are updated.

This document is planning/tracking only and does not itself mark delivery complete.
