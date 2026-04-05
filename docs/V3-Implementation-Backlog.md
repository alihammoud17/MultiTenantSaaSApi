# V3 Implementation Backlog

This backlog is based on the current repository state as of April 5, 2026. It treats the existing .NET API as the system of record, keeps provider-specific billing logic inside `BillingService`, and builds from functionality that already exists in code rather than proposing a rewrite.

## Current repository baseline

### Completed V2 scope already present in the codebase

The current repository has already completed the major V2 platform slices:

- **Tenant registration and tenant context enforcement** are implemented in the .NET API, including active-tenant checks and tenant resolution from subdomain, request header, and JWT claims.
- **Authentication and refresh token lifecycle** are implemented, including refresh token issuance, rotation, logout, and authenticated revocation.
- **RBAC foundation** is implemented with roles, permissions, policy registration, authorization handlers, and tenant-scoped role assignments.
- **Tenant administration** is implemented for tenant detail retrieval, user listing, user creation, user role changes, user deletion, and tenant audit log access.
- **Plan catalog and subscription upgrade flow** are implemented in the .NET API, with subscription lifecycle state persisted on the subscription aggregate.
- **Plan-based API usage limiting** is implemented through Redis-backed per-tenant rate limiting with safe fallback behavior.
- **Tenant-scoped audit logging** is implemented for auth, plan, and tenant administration flows.
- **Observability baseline** is implemented through correlation ids, structured request logging, health checks, metrics, and activity instrumentation.
- **Initial billing architecture split** is already visible:
  - the .NET API exposes a signed internal billing callback endpoint,
  - the .NET API validates tenant/subscription mapping before applying events,
  - the .NET API stores billing inbox records for idempotency,
  - `BillingService` already contains the provider adapter boundary, webhook handler shell, normalized billing event types, durable file-backed queueing, retry/backoff and dead-letter handling, replay-safe deduplication, and drift-aware reconciliation scaffolding.
- **Automated test coverage** already exists across auth, RBAC, tenant admin, audit logging, observability, and internal billing callback flows.
  - Security-focused suites now also cover authentication negatives, authorization denials, tenant/header tampering, validation abuse paths, refresh/revoke replay behavior, and internal billing signature/timestamp rejection scenarios.

### What is still intentionally incomplete

The codebase has V3 groundwork, but the production billing path is not complete yet:

- `BillingService` does not yet verify real provider webhooks.
- `BillingService` does not yet publish authenticated callbacks into the .NET API.
- `BillingService` now has a durable workflow foundation (file-backed queue + retry/dead-letter + replay-safe dedup) plus drift-aware reconciliation comparison logic. This iteration is documented and operationalized, but it still needs live provider/.NET state readers and provider-connected callback delivery for production readiness.
- The .NET API does not yet expose tenant-facing billing self-service endpoints beyond plan upgrade and subscription state enforcement.
- Entitlements, add-ons, usage analytics exports, outbound webhooks, and deeper billing reconciliation workflows are not yet implemented.

## Durable workflow iteration checkpoint (April 5, 2026)

A documentation-focused operational checkpoint for durable billing workflows is now complete:

- durable workflow behavior and operator procedures are documented in `docs/Billing-Workflow-Runbook.md`
- `BillingService/README.md` and root `README.md` now reflect the current durability/reconciliation status and pre-live limitations
- setup/config/test instructions for this iteration are now explicit in:
  - root `README.md` (`BillingService` run/test commands + durability env overrides)
  - `BillingService/README.md` (durable workflow env template + test behavior note)
  - `docs/Billing-Workflow-Runbook.md` (operator baseline runtime values and triage procedures)
- backlog language has been aligned to treat this durability slice as implemented scaffolding, not live provider billing

Remaining gap to close before production billing cutover: live provider integration, webhook verification, and authenticated callback delivery to the .NET internal billing endpoint.

## V3 priorities in implementation order

The implementation order below is driven by current dependencies in the repo.

1. **Finish the internal billing service-to-service path** between `BillingService` and the .NET API.
2. **Add real provider webhook verification and normalization** inside `BillingService`.
3. **Make billing workflows durable and observable** with persistent event storage, replay protection, and reconciliation support.
4. **Expose tenant-facing billing self-service capabilities** from the .NET API using the subscription state it already owns.
5. **Add entitlements and feature gating** on top of the existing plan/subscription model.
6. **Strengthen security, analytics, and outbound platform integrations** after the billing core is stable.

That order keeps the current architecture intact:

- provider-specific code stays in `BillingService`,
- internal subscription state remains owned by the .NET API,
- tenant validation and authorization remain in the .NET API,
- mirrored billing data is added only when ownership is explicit.

## Iteration plan

---

## Iteration 1 - Authenticated BillingService to .NET callback delivery

### Goal
Turn the existing callback contract into a real service-to-service integration by letting `BillingService` publish normalized subscription events to `POST /api/internal/billing/subscription-events` using the shared HMAC signature scheme that the .NET API already enforces.

### Why this is first
The .NET API callback endpoint, signature validator, request contract, idempotency table, and lifecycle application logic already exist. `BillingService` also already has normalized event types and a callback payload shape. The smallest V3 slice is to join those two halves.

### Dependencies
- Existing .NET callback contract and signature validation.
- Existing `BillingService` `SubscriptionSyncJob` and callback payload types.
- Configuration for callback base URL and shared secret.

### Concrete implementation scope
- Add a real `BillingCallbackPublisher` in `BillingService` that:
  - signs requests with the same timestamp + body HMAC format used by the .NET API,
  - posts the callback payload to the internal .NET endpoint,
  - emits structured logs with correlation id, tenant id, subscription id, and event id,
  - treats non-success responses as retryable or terminal in a clearly testable way.
- Wire the publisher into `SubscriptionSyncJob` behind configuration.
- Document required configuration in `BillingService/README.md` and root README if local setup changes.
- Keep the publisher isolated from provider-specific code.

### Acceptance criteria
- A normalized event produced in `BillingService` is sent to the .NET callback endpoint with a valid signature.
- Successful callback delivery updates the .NET subscription state and records the inbox event once.
- Duplicate delivery does not double-apply the state transition.
- Invalid callback configuration fails clearly in logs without leaking secrets.
- Automated tests cover signature generation, callback publishing behavior, and at least one end-to-end happy path across the internal contract.

---

## Iteration 2 - Provider webhook verification and normalization in BillingService

### Goal
Replace the placeholder provider adapter flow with the first real provider implementation that verifies webhook authenticity, rejects unsupported payloads, resolves internal tenant/subscription mapping safely, and emits normalized internal lifecycle events.

### Why it follows Iteration 1
Webhook normalization should not ship before the internal callback path exists. Once BillingService can deliver internal events safely, provider ingestion can be added without changing the .NET API contract.

### Dependencies
- Iteration 1 callback publisher.
- Current provider adapter abstraction and webhook handler.
- A documented mapping strategy for provider identifiers to internal tenant/subscription identifiers.

### Concrete implementation scope
- Implement one concrete provider adapter behind the existing `BillingProviderAdapter` interface.
- Verify webhook signatures before any mapping or event acceptance.
- Normalize supported provider events into the event types already accepted by the .NET API contract:
  - `subscription.activated`
  - `subscription.renewed`
  - `subscription.plan_changed`
  - `subscription.downgrade_scheduled`
  - `subscription.canceled`
  - `subscription.grace_period_started`
  - `subscription.grace_period_expired`
  - `subscription.expired`
  - `invoice.payment_failed`
- Keep raw provider payloads out of the .NET API domain model.
- Reject events that cannot be mapped to a validated tenant and subscription.

### Acceptance criteria
- Invalid webhook signatures are rejected before normalization.
- Supported provider events produce normalized internal events aligned with the existing internal billing contract.
- Unsupported or unmapped provider events are logged and ignored without mutating .NET state.
- Tenant mapping is validated before callbacks are sent to the .NET API.
- Tests cover valid webhook acceptance, invalid signature rejection, replay/duplicate handling at the service boundary, and event normalization.

---

## Iteration 3 - Durable billing workflow processing and reconciliation

### Goal
Operationalize durable billing processing in `BillingService` with restart-safe workflow state, replay protection, dead-letter visibility, and reconciliation support against provider/internal snapshots.

### Why it follows Iteration 2
Durability matters most once real provider traffic exists. The repository now includes durable scaffolding and runbook-level operational guidance, but still needs live provider and callback wiring before production use.

### Dependencies
- Iteration 2 real webhook ingestion.
- Explicit storage ownership for provider-facing event records in `BillingService`.
- Stable internal callback contract in the .NET API.

### Concrete implementation scope
- Preserve and evolve persistent webhook/event workflow state in `BillingService` for:
  - received external event id,
  - provider,
  - normalized event type,
  - tenant/subscription mapping result,
  - processing status,
  - retry count,
  - timestamps needed for diagnostics and replay.
- Replace in-memory duplicate suppression in `SubscriptionSyncJob` with durable replay protection.
- Add retry-safe worker processing for callback delivery.
- Add reconciliation commands or jobs that compare provider state with internal callback/application status.
- Keep the .NET API idempotency layer in place; do not rely on only one side for deduplication.

### Acceptance criteria
- BillingService can restart without losing knowledge of processed, pending, or failed workflow events.
- Duplicate provider events are persisted and handled idempotently.
- Failed callback deliveries can be retried without double-applying subscription transitions in the .NET API.
- Reconciliation output is observable enough to diagnose mismatches between provider state and internal subscription state.
- Tests cover retry behavior, durable duplicate suppression, and reconciliation edge cases where practical.

---

## Iteration 4 - Tenant-facing billing self-service on top of internal subscription state

### Goal
Expose concrete tenant-facing billing capabilities from the .NET API using the subscription and lifecycle state it already owns, while keeping provider operations delegated through BillingService.

### Why it is next
Once internal billing events are trustworthy and durable, the platform can safely expose more billing state to tenants without coupling clients to provider payloads.

### Dependencies
- Stable subscription lifecycle updates from Iterations 1 through 3.
- Existing tenant context enforcement and RBAC.
- Clear ownership rules for any mirrored billing data exposed to tenants.

### Concrete implementation scope
- Add tenant-scoped read endpoints for current subscription/billing status, including:
  - current plan,
  - subscription lifecycle state,
  - current billing period,
  - scheduled downgrade information,
  - grace-period deadlines where applicable.
- Evaluate whether plan change requests should become intent-based when a provider-backed billing step is required.
- If self-service mutation endpoints are added, keep them thin and route provider-facing work through BillingService or an explicit internal orchestration boundary.
- Audit all tenant-initiated billing mutations.

### Acceptance criteria
- Tenants can retrieve their own billing status without seeing cross-tenant data.
- Billing read models reflect the internal subscription state already persisted by the .NET API.
- Any provider-backed self-service action remains authenticated, authorized, auditable, and tenant-scoped.
- Tests cover tenant isolation, authorization, and lifecycle-state correctness for exposed billing data.

---

## Iteration 5 - Entitlements and feature gating tied to plans and subscription lifecycle

### Goal
Extend the current plan model into explicit entitlements and feature gating so the platform can enforce more than API call quotas.

### Why it follows billing self-service
The repo already has plan records, plan-based rate limiting, and subscription lifecycle state. Entitlements should build on that confirmed billing state rather than precede it.

### Dependencies
- Stable subscription lifecycle state.
- Existing RBAC and tenant context enforcement.
- A clear ownership model for entitlement definitions versus tenant assignment state.

### Concrete implementation scope
- Introduce explicit entitlement definitions associated with plans and, if needed later, add-on assignments.
- Add application-layer services for entitlement evaluation.
- Reuse the current tenant-scoped enforcement pattern used by rate limiting.
- Avoid embedding provider-specific assumptions into entitlement checks.
- Add targeted API or service-layer gates where the product already needs plan-aware behavior.

### Acceptance criteria
- Entitlement evaluation is tenant-scoped and derived from internal subscription state.
- Feature gates fail closed when a tenant lacks the required entitlement.
- Plan changes and lifecycle transitions update effective entitlements predictably.
- Tests cover tenant isolation, entitlement transitions, and denial of unauthorized feature access.

---

## Iteration 6 - Security hardening, analytics, outbound webhooks, and operational maturity

### Goal
Finish V3 by strengthening security-sensitive paths and making the billing/platform surface easier to operate in production.

### Why it is last
These improvements depend on the billing core, tenant self-service surface, and entitlement model being stable enough to instrument and expose externally.

### Dependencies
- Stable billing workflows and internal event semantics.
- Existing observability baseline in both services.
- Clear contracts for any outbound event payloads.

### Concrete implementation scope
- Harden service-to-service authentication and secret/configuration handling where needed.
- Expand structured metrics and logs around webhook rejection reasons, callback failures, retries, reconciliation outcomes, and entitlement transitions.
- Add tenant-safe usage analytics exports or read models if the product needs them.
- Add outbound webhooks using explicit, versioned contracts and replay-safe delivery.
- Add runbook-oriented documentation for failure handling, retries, replay, and reconciliation.

### Acceptance criteria
- New operational telemetry makes billing failures diagnosable without exposing secrets.
- Outbound webhooks, if added, are authenticated, versioned, replay-safe, and tenant-safe.
- Security-sensitive billing and account flows have focused automated test coverage.
- Documentation clearly explains configuration, troubleshooting, and ownership boundaries.

## Cross-cutting V3 rules for every iteration

Each iteration should continue to follow the repository rules already established in code and docs:

- Keep the .NET API as the system of record for tenant and subscription state.
- Keep provider-specific webhook and billing logic inside `BillingService`.
- Never trust external provider identifiers alone for tenant mapping.
- Preserve idempotency on both sides of the internal billing boundary.
- Add tenant-isolation and authorization tests for every new tenant-facing or admin-facing surface.
- Update README and feature docs whenever the implemented behavior changes.
- Prefer additive slices over broad rewrites.

## Recommended validation per iteration

### .NET API changes
- `dotnet build MultiTenantSaaSApi.sln`
- `dotnet test MultiTenantSaaSApi.sln`

### BillingService changes
- `cd BillingService && npm run build`
- `cd BillingService && npm test`

## Should this repo have a root-level `PLANS.md`?

**Yes, but only as a lightweight navigation layer.**

A root-level `PLANS.md` would help this repository because work is now split across two execution boundaries:

- the .NET API system of record,
- the `BillingService` provider-integration boundary,
- plus repo-level docs that track staged delivery.

To stay useful, `PLANS.md` should not duplicate the detailed backlog in this file. It should instead provide:

- a short status snapshot,
- links to `docs/V2-Implementation-Backlog.md`, this V3 backlog, and `docs/Internal-Billing-Contract.md`,
- the current implementation phase and next recommended iteration,
- a brief ownership reminder for `.NET API` vs `BillingService`.

That would make the repo easier to navigate for future contributors without creating another source of truth to keep in sync.
