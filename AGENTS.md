# AGENTS.md

## Project overview
This repository contains a multi-tenant SaaS platform with:
- a primary ASP.NET Core API as the system of record
- a planned Node.js companion service for billing, webhooks, and subscription workflow processing

## Current stable scope
The existing .NET API already includes:
- tenant registration/authentication
- tenant context enforcement middleware
- plan model and upgrade flow
- plan-based API usage limits
- tenant-scoped audit logs
- health checks
- automated tests

## Current V2 direction
The next phase focuses on billing architecture and subscription lifecycle support.

Primary V2 goals:
- preserve the .NET API as the core system of record
- add a clean billing boundary instead of mixing provider logic into core business code
- introduce a Node.js companion service for billing/webhooks/background workflow handling
- keep all tenant and authorization guarantees intact across services

## Service boundaries

### .NET API owns
- tenant registration and authentication
- tenant context enforcement
- RBAC / authorization
- tenant-scoped business data
- plan enforcement decisions
- audit logging
- admin endpoints
- internal persistence models and migrations for core SaaS data

### Node.js billing service owns
- billing provider integration
- webhook ingestion and signature verification
- subscription lifecycle workflow handling
- retry-safe background processing for billing events
- provider-specific adapters and mapping logic
- outbound internal events/callbacks toward the .NET API

## Architecture rules
- Keep the .NET API as the source of truth for tenant and subscription state exposed to the rest of the application.
- Do not move tenant enforcement or authorization logic into the Node.js billing service.
- Do not couple provider-specific payloads directly to core .NET domain models.
- Keep provider logic isolated behind adapters.
- Prefer additive changes over rewrites.
- Keep changes incremental and reviewable.
- Avoid unrelated refactors.
- Preserve working V1 behavior unless the task explicitly changes it.

## Multi-tenant safety rules
- Never return cross-tenant data.
- Every new endpoint must enforce tenant scoping explicitly.
- Every billing event applied to the .NET API must resolve to a validated tenant mapping.
- Never trust raw external billing payloads as tenant identifiers without internal verification.
- Add tenant-isolation tests for any new auth, admin, billing, or subscription behavior.

## Billing integration rules
- Treat webhook processing as untrusted external input.
- Verify webhook authenticity before processing.
- Make billing event handling idempotent.
- Avoid double-processing renewals, cancellations, downgrades, or failed payment events.
- Store provider event identifiers when needed to support idempotency and replay safety.
- Keep internal billing contracts stable even if provider payloads change.
- Prefer explicit status mapping between provider subscription states and internal subscription states.

## Contract rules between .NET and Node.js
- Keep the .NET <-> Node contract explicit and versionable.
- Prefer internal DTOs/events over leaking provider payloads across services.
- Every contract must include enough data to:
  - identify the tenant safely
  - identify the subscription safely
  - determine the event type
  - support idempotent processing
  - support traceability in logs
- Do not let the Node.js service modify unrelated tenant/business records.
- Any internal callback or message must be authenticated/validated.

## Data ownership rules
- Core tenant/user/business data belongs to the .NET API.
- Provider-facing billing integration data belongs to the Node.js billing service unless there is a clear reason otherwise.
- If subscription status is mirrored in both services, the ownership and synchronization direction must be explicit in code and documentation.
- Avoid ambiguous write ownership.

## Background job rules
- All background billing/subscription jobs must be retry-safe.
- Jobs must be designed to handle duplicate delivery.
- Long-running workflow logic should stay out of controllers.
- Prefer dedicated services/handlers for lifecycle actions such as:
  - renewal handling
  - failed payment handling
  - downgrade scheduling
  - cancellation handling
  - grace period expiration

## Implementation expectations
For any non-trivial task:
1. Analyze the current implementation first.
2. Identify exact files/classes to change before broad edits.
3. Propose the smallest safe implementation slice.
4. Implement only the requested scope.
5. Add or update automated tests.
6. Run validation commands.
7. Summarize changed files, risks, and follow-up work.

## Prompt execution style
When a task is architecture-heavy or spans both services:
- plan before coding
- identify service boundaries first
- call out assumptions explicitly
- avoid implementing .NET and Node rewrites in one large step unless explicitly requested

## Validation
Run the relevant commands after changes.

### For .NET work
- dotnet build
- dotnet test

### For Node.js work
- run the project-specific install/build/test commands if present
- if the Node.js service is newly scaffolded, document the exact commands added

If schema changes are introduced:
- add the migration
- name the migration clearly
- summarize what changed in the schema

## Testing expectations
Add tests for:
- tenant isolation
- authorization on admin/billing-sensitive actions
- idempotent billing event handling
- invalid or replayed webhook behavior
- subscription state transitions
- failure and retry paths where practical

## Coding guidelines
- Prefer small diffs.
- Prefer service-layer logic over bloated controllers.
- Keep DTOs, persistence models, handlers, and provider adapters separated.
- Keep error responses consistent with existing API patterns.
- Do not introduce circular dependencies between projects/services.
- Do not embed billing-provider assumptions in unrelated domain code.

## Observability expectations
For billing and subscription work:
- emit structured logs
- include correlation/trace identifiers where possible
- log lifecycle transitions and rejected events clearly
- make failures diagnosable without exposing secrets

## Secrets and config
- Never hardcode provider secrets or signing secrets.
- Use environment variables or existing configuration conventions.
- Do not log secrets or raw sensitive credentials.
- Document any new required configuration keys.

## Done when
A task is complete only when:
- the requested behavior is implemented
- the change respects service boundaries
- build passes
- tests pass, or failures are clearly explained
- changed files are summarized
- risks, assumptions, and follow-up work are listed explicitly
