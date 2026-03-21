# AGENTS.md

## Project overview
This repository contains a multi-tenant SaaS platform with:
- a primary ASP.NET Core API as the system of record
- a Node.js BillingService responsible for provider-facing billing integration and billing workflow processing

## Current project status
V1 and V2 are complete.

### Completed platform scope
- tenant registration and authentication
- tenant context enforcement
- refresh tokens and token revocation
- RBAC / role-permission model
- plan model and upgrade flow
- plan-based API usage limits
- tenant-scoped audit logging
- admin endpoints for tenant/user management
- health checks
- automated tests
- initial observability foundation
- initial billing architecture split between .NET and BillingService

## Current V3 direction
V3 focuses on productionizing billing, improving operational durability, strengthening security, and expanding platform maturity.

### Primary V3 priorities
- live billing provider integration
- durable billing workflows and reconciliation
- tenant-facing billing self-service capabilities
- entitlements / add-ons / feature gating
- identity and security hardening
- stronger observability and runbooks
- usage analytics and outbound webhooks

## System boundaries

### .NET API owns
- tenant registration and authentication
- refresh tokens, revocation, and session-related core state
- tenant context enforcement
- RBAC / authorization
- tenant-scoped business data
- plan enforcement and entitlement enforcement
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
- Do not move tenant enforcement or authorization into BillingService.
- Do not leak raw provider payloads into unrelated .NET domain models.
- Keep provider-specific logic isolated behind adapters or handlers.
- Prefer additive, reviewable changes over broad rewrites.
- Avoid unrelated refactors.
- Preserve existing working behavior unless the task explicitly changes it.
- Keep contracts explicit and versionable.

## Multi-tenant safety rules
- Never return cross-tenant data.
- Every new endpoint must enforce tenant scoping explicitly.
- Every billing or subscription event applied internally must resolve to a validated tenant mapping.
- Never trust external payload identifiers without internal verification.
- Add tenant-isolation tests for any new auth, admin, billing, entitlement, analytics, or webhook behavior.

## Billing rules
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
- Any internal callback, webhook relay, or service-to-service action must be authenticated/validated.

## Data ownership rules
- Core tenant, user, RBAC, and business data belong to the .NET API.
- Provider-facing billing integration data belongs to BillingService unless explicitly mirrored.
- If subscription or invoice data is mirrored across services, ownership and synchronization direction must be explicit in code and docs.
- Avoid ambiguous write ownership.

## V3 execution style
For any non-trivial V3 task:
1. Read this file and inspect the current implementation first.
2. Review the relevant docs before coding.
3. Plan the smallest safe slice first.
4. Implement only the requested scope.
5. Add or update automated tests.
6. Update documentation for that iteration.
7. Run validation commands.
8. Summarize changed files, assumptions, risks, and follow-up work.

## Required documentation workflow
Every completed iteration must update docs as applicable.

Always review and update:
- README.md
- docs/V3-Implementation-Backlog.md
- docs/Internal-Billing-Contract.md if contracts changed
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

## Validation
Run the relevant commands after changes.

### For .NET work
- dotnet build
- dotnet test

### For BillingService work
- run the project-specific install/build/test commands documented in BillingService
- if commands change, update BillingService/README.md

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
- Keep local development and testability in mind.

## Observability expectations
For V3 work:
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

## Done when
A task is complete only when:
- the requested behavior is implemented
- the change respects system boundaries
- build passes
- tests pass, or failures are clearly explained
- docs/readme are updated for that iteration
- changed files are summarized
- remaining risks, assumptions, and follow-up work are listed explicitly