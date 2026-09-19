# AGENTS.md

## Project overview
This repository contains a multi-tenant SaaS platform with:
- a primary ASP.NET Core API as the system of record
- a Node.js BillingService as the provider-facing billing companion service

## Current project status
V1, V2, and V3 are complete. V4 established the current pre-deployment, code-first engineering maturity baseline; historical phase plans are archived under `docs/archive/` and are not active sources of truth.

Use `README.md`, `BillingService/README.md`, and the current architecture, operations, contracts, and decisions documents under `docs/` alongside the implementation to establish current behavior. Completed phases do not imply production readiness or live billing integration.

## Agent working environment
- The maintainer uses Codex in the desktop application and sometimes the Visual Studio Code extension; browser-hosted Codex is no longer the working environment.
- Work from the active local checkout. Inspect the current branch and worktree before editing, and preserve unrelated local changes.
- Do not assume a browser/cloud container, a Linux host, preinstalled tools, or access to local services. Check the available SDKs, tools, and connections before validation.
- Local development and the Ubuntu deployment target are separate environments. Keep commands portable where practical and report environment-specific blockers.
- Read `BillingService/AGENTS.md` as well when working under `BillingService/`.

## Current direction (pre-deployment / code-first)
Current work is focused on pre-deployment engineering maturity.

### North star
Build a production-like local platform that is:
- code-first
- pre-deployment
- deterministic to validate locally
- contract-safe and tenant-safe by default

### Priorities
- deterministic local bootstrap and smoke validation workflows
- cross-service contract conformance tests (.NET API <-> BillingService)
- replay/idempotency fixture-driven billing validation
- tenant-isolation invariant testing across sensitive surfaces
- documentation and runbook accuracy for every completed slice

## System boundaries

### .NET API owns
- tenant registration and authentication
- refresh tokens, revocation, and session-related core state
- tenant context enforcement
- RBAC / authorization
- tenant-scoped business data
- plan and entitlement enforcement
- admin endpoints
- audit logging
- internal subscription state exposed to the rest of the platform
- core persistence models and migrations

### BillingService owns
- billing provider integration
- provider webhook ingestion and verification
- billing workflow processing
- provider-to-internal event normalization
- retry-safe billing jobs
- reconciliation with provider state
- provider-specific adapters and mapping logic

## Architecture rules
- Keep the .NET API as the system of record unless the task explicitly changes ownership.
- Keep BillingService as the provider-facing companion service.
- Do not move tenant enforcement or authorization into BillingService.
- Do not leak raw provider payloads into unrelated .NET domain models.
- Keep provider-specific logic isolated behind adapters or handlers.
- Keep contracts explicit and versionable.
- Prefer additive, reviewable thin vertical slices.
- Avoid broad rewrites or unrelated refactors.
- Preserve existing working behavior unless the task explicitly changes it.

## Multi-tenant safety rules
- Never return cross-tenant data.
- Every new endpoint must enforce tenant scoping explicitly.
- Every billing or subscription event applied internally must resolve to a validated tenant mapping.
- Never trust external payload identifiers without internal verification.
- Add tenant-isolation tests for any new auth, admin, billing, entitlement, analytics, or webhook behavior.

## Billing safety rules
- Treat every provider webhook as untrusted input until verified.
- Make billing event handling idempotent.
- Avoid double-processing renewals, cancellations, downgrades, reactivations, and failed payments.
- Store external event identifiers when needed for replay protection and reconciliation.
- Prefer explicit mapping between provider states and internal subscription states.
- Keep retry and reconciliation behavior observable and testable.

## Contract rules
- Keep .NET <-> BillingService contracts explicit and stable.
- Prefer internal DTOs/events over raw provider payload sharing.
- Every contract should include enough data to:
  - identify the tenant safely
  - identify the subscription safely
  - identify the event type
  - support idempotent processing
  - support traceability in logs
- Any internal callback, webhook relay, or service-to-service action must be authenticated and validated.

## Data ownership rules
- Core tenant, user, RBAC, and business data belong to the .NET API.
- Provider-facing billing integration data belongs to BillingService unless explicitly mirrored.
- If subscription or invoice data is mirrored across services, ownership and synchronization direction must be explicit in code and docs.
- Avoid ambiguous write ownership.

## Execution style
For any non-trivial task:
1. Read this file and inspect the current implementation first.
2. Review the relevant docs before coding.
3. Plan the smallest safe thin vertical slice first.
4. Implement only the requested scope.
5. Add or update automated tests when behavior changes; for documentation-only changes, verify instructions against code, scripts, and referenced paths.
6. Run explicit local validation commands and record results.
7. Update documentation for that completed iteration.
8. Summarize changed files, assumptions, risks, and follow-up work.

## Required documentation workflow
Every completed iteration must update docs as applicable.

Review the following and update those affected by the change; do not make unrelated documentation edits solely to touch each file:
- README.md
- docs/architecture.md when system behavior or ownership changed
- docs/operations.md when configuration or operating procedures changed
- docs/contracts.md if contracts changed
- docs/decisions.md when a durable decision or unresolved deployment choice changed
- docs/CHANGELOG.md for completed milestone-level work
- BillingService/README.md if BillingService changed
- any feature-specific docs added under docs/

Do not document unfinished work as completed.
Do not leave the README or docs stale after implementation.

## Testing expectations
Add or update tests for:
- tenant isolation
- authorization on sensitive actions
- idempotent billing event handling
- invalid or replayed webhook behavior
- subscription and entitlement transitions
- retry and reconciliation behavior where practical
- security-sensitive auth/account flows where applicable
- deterministic local workflow/smoke coverage when affected

## Validation
Run the relevant commands after changes.

For documentation-only changes, run `git diff --check` and verify referenced paths and commands against the repository. Application builds/tests are not required unless runtime code or configuration also changes. Report what was and was not run.

The standardized local workflow is `scripts/dev.sh` (`bootstrap`, `seed`, `run`, `smoke`, `test`). `reset` drops the local database and clears workflow state; use it only when that reset is within the requested scope. Smoke checks cover placeholder billing acceptance, not live provider-to-API delivery.

### For .NET work
Run:
- dotnet --info
- /tmp/dotnet-tools/dotnet-ef --version
- dotnet restore
- dotnet build --no-restore
- dotnet test --no-build --verbosity normal

### For BillingService work
- run the project-specific install/build/test commands documented in BillingService
- if commands change, update BillingService/README.md

The current prerequisites are .NET 10, Node.js 22 or later, PostgreSQL 16 or later, and Redis 7 or later. The full test wrapper currently expects `/tmp/dotnet-tools/dotnet-ef`; do not assume this temporary installation exists on the local machine. The bootstrap/reset/seed scripts support `DOTNET_EF_BIN`; manual EF commands can use an available `dotnet ef` installation.

## EF Core
When a schema/model change requires a migration, you may use:
- /tmp/dotnet-tools/dotnet-ef migrations add <MigrationName> --project Infrastructure --startup-project Presentation
- /tmp/dotnet-tools/dotnet-ef database update --project Infrastructure --startup-project Presentation

If the DbContext is not in the startup project, specify:
- --project <path-to-ef-project>
- --startup-project <path-to-startup-project>
The DbContext and migrations are in `Infrastructure`; the startup host is `Presentation`.

If schema changes are introduced:
- add the migration
- use a clear migration name
- summarize what changed in the schema

## Coding guidelines
- Prefer small diffs.
- Prefer service-layer logic over bloated controllers.
- Keep DTOs, domain models, persistence models, handlers, provider adapters, and workflow logic separated.
- Keep error responses consistent with existing API patterns.
- Do not introduce circular dependencies.
- Do not embed billing-provider assumptions in unrelated platform code.
- Keep local development and deterministic testability in mind.

## Observability expectations
For pre-deployment work:
- emit structured logs
- include correlation or trace identifiers where possible
- log lifecycle transitions and rejection reasons clearly
- keep failures diagnosable without exposing secrets
- update docs when new metrics, traces, alerts, or runbooks are introduced

## Secrets and configuration
- Never hardcode secrets.
- Use environment variables or the repository’s existing configuration conventions.
- Do not log secrets, raw tokens, or signing secrets.
- Document any new configuration keys or environment variables.

## Docker and deployment rules
- Do not commit real secrets, passwords, tokens, API keys, JWT secrets, provider secrets, or database passwords.
- Use .env.example files only for documentation.
- Runtime secrets must stay outside the repository, preferably under /etc/multitenant-saas-api/.
- Prefer Docker Compose for the local Ubuntu VM deployment.
- Do not introduce Kubernetes yet.
- Do not remove existing local scripts unless explicitly requested.
- Do not change application behavior unless deployment requires it.
- Keep Dockerfiles multi-stage where possible.
- The existing Compose profile targets a local Ubuntu VM; Ubuntu 24.04 is the intended local VM target, not an assumption about the developer's host OS.
- A future VPS may reuse this structure, but deployment topology, TLS/DNS, secret storage, and recovery choices remain open in `docs/decisions.md`.
- Always report validation commands executed and their result.
- If a command cannot be executed in the Codex environment, explain why.

## Done when
A task is complete only when:
- the requested behavior is implemented
- the change respects system boundaries
- thin-slice scope was maintained (no broad refactor)
- explicit validation was executed and results were reported
- relevant tests pass, or failures/skipped checks are clearly explained
- affected docs/readme are updated for that iteration
- changed files are summarized
- remaining risks, assumptions, and follow-up work are listed explicitly
