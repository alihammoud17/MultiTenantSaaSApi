# V2 Implementation Backlog (Incremental, Production-Oriented)

This backlog translates the current repository state into a V2 delivery plan without rewriting working V1 behavior.

## Current Baseline (from repository)
- Auth: register/login with short-lived JWT access token only (`/api/auth/register`, `/api/auth/login`).
- Tenant scoping: middleware resolves tenant from subdomain/header/JWT claim and blocks missing/suspended tenants.
- Plans/subscriptions: plan catalog + upgrade endpoint; one subscription per tenant.
- Usage control: plan-based per-tenant rate limiting via Redis.
- Audit: tenant-scoped audit write/read.
- Tests: integration tests for core endpoints and unit tests for services.

---

## Backlog Items

### 1) Refresh Tokens + Revocation
**Goal**
- Add secure session continuation so clients can renew access tokens without re-login, and allow server-side session revocation (logout/all-sessions controls later).

**Affected files/projects**
- `Domain/Entites/User.cs` (session linkage fields if needed).
- New entities in `Domain/Entites/` (e.g., `RefreshToken`, optional `RevokedToken`).
- `Infrastructure/Data/ApplicationDbContext.cs` (DbSet, mapping/indexes, relationships).
- `Application/Services/JwtService.cs` and `Domain/Interfaces/IJwtService.cs` (access+refresh issuance and refresh validation).
- `Presentation/Controllers/AuthController.cs` (refresh/logout endpoints).
- `Presentation/Program.cs` (DI for token/session service).
- `Tests/Integration/ApiEndpointsTests.cs` + new auth session tests.

**Schema changes**
- Add `RefreshTokens` table (minimum):
  - `Id`, `TenantId`, `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt`, `ReplacedByTokenId`, `CreatedByIp`, `RevokedByIp`.
- Indexes:
  - `(TenantId, UserId)` for lookup.
  - Unique on `TokenHash`.
  - Optional filtered index on active tokens (`RevokedAt IS NULL`).

**API changes**
- `POST /api/auth/refresh` (rotate refresh token + issue new access token).
- `POST /api/auth/logout` (revoke provided refresh token).
- Keep existing register/login responses backward compatible; optionally extend with refresh token fields behind explicit response contract versioning.

**Services to add or modify**
- Add `ITokenSessionService` (issue/rotate/revoke refresh tokens).
- Extend JWT service with helper methods for access token generation and claim consistency.
- Ensure tenant context is preserved in refreshed access tokens.

**Tests to add**
- Integration:
  - refresh success,
  - refresh with revoked token fails,
  - refresh with expired token fails,
  - token rotation invalidates previous token.
- Unit:
  - hash/compare refresh token,
  - revocation logic,
  - tenant mismatch rejection.

**Risk level**
- **High** (auth/session surface and token security).

**Rollout order**
- **Phase 1** (first V2 item; required dependency for secure long-lived sessions).

---

### 2) RBAC (Roles/Permissions)
**Goal**
- Replace string-only role checks with explicit tenant-scoped role/permission model suitable for admin and future feature authorization.

**Affected files/projects**
- `Domain/Entites/User.cs` (remove hard dependency on free-form role string over time; keep compatibility field during transition).
- New entities in `Domain/Entites/` (e.g., `Role`, `Permission`, `UserRole`, `RolePermission`).
- `Infrastructure/Data/ApplicationDbContext.cs` (model config + seed baseline roles/permissions).
- `Presentation/Program.cs` (authorization policies).
- `Presentation/Controllers/*` (policy-based `[Authorize(Policy=...)]` updates where needed).
- `Application/Services/` new `AuthorizationService`/claims enrichment helper.
- Tests in `Tests/Integration` and `Tests/UnitTests`.

**Schema changes**
- Add tables:
  - `Roles` (tenant-scoped or system roles with tenant binding policy).
  - `Permissions`.
  - `UserRoles` (many-to-many User↔Role).
  - `RolePermissions` (many-to-many Role↔Permission).
- Add constraints to ensure role assignments cannot cross tenants.

**API changes**
- No breaking changes required initially.
- Add tenant-admin endpoints later for role assignment (can be bundled with admin feature item).

**Services to add or modify**
- Add `IRbacService` for permission resolution.
- Add authorization policy handlers for permission checks.
- Update token generation strategy to include minimal claims + server-side permission resolution where appropriate.

**Tests to add**
- Integration:
  - permitted action succeeds,
  - missing permission returns 403,
  - cross-tenant role assignment blocked.
- Unit:
  - permission aggregation,
  - policy handler behavior.

**Risk level**
- **High** (authorization correctness and tenant isolation).

**Rollout order**
- **Phase 2** (after refresh sessions, before broad admin endpoint expansion).

---

### 3) Admin Endpoints for Tenant/User Management
**Goal**
- Provide authorized tenant-admin APIs to manage tenant users and basic tenant settings while strictly enforcing tenant boundaries.

**Affected files/projects**
- New controllers under `Presentation/Controllers/` (e.g., `TenantAdminController`, `UsersController`).
- New DTOs in `Domain/DTOs/` for create/update/list operations.
- `Application/Services/` user-management service(s).
- `Infrastructure/Data/ApplicationDbContext.cs` queries/index support.
- `Domain/Interfaces/` service contracts.
- Integration tests in `Tests/Integration/ApiEndpointsTests.cs` or split admin-specific file.

**Schema changes**
- Optional incremental additions on `Users`:
  - `Status` (Active/Invited/Suspended),
  - `InvitedAt`,
  - `DeactivatedAt`.
- Optional audit metadata fields if missing for admin operations.

**API changes**
- Proposed incremental endpoints:
  - `GET /api/admin/users` (tenant-scoped list),
  - `POST /api/admin/users` (create/invite user),
  - `PATCH /api/admin/users/{id}` (status/role updates),
  - `GET /api/admin/tenant` and `PATCH /api/admin/tenant` (basic settings).
- Require authentication + RBAC policy (`tenant.admin.*`).

**Services to add or modify**
- Add `ITenantAdminService` and/or `IUserManagementService`.
- Reuse `IAuditService` to log all admin mutations.
- Enforce tenant-scoped query patterns only (`WHERE TenantId = currentTenantId`).

**Tests to add**
- Integration:
  - admin can CRUD within own tenant,
  - non-admin forbidden,
  - tenant A cannot access tenant B users,
  - audit events are emitted.
- Unit:
  - validation and duplicate email rules per tenant.

**Risk level**
- **Medium-High** (new write endpoints + security boundaries).

**Rollout order**
- **Phase 3** (depends on RBAC policies).

---

### 4) Billing Integration (Provider-Ready)
**Goal**
- Connect subscription lifecycle to external billing provider while keeping plan enforcement authoritative in core .NET API.

**Affected files/projects**
- `Domain/Entites/Subscription.cs` and new billing entities (customer/subscription mapping, invoice/event logs).
- `Infrastructure/Data/ApplicationDbContext.cs` for mapping and indexes.
- `Application/Services/` billing abstraction (`IBillingGateway`, `BillingService`).
- `Presentation/Controllers/PlansController.cs` (upgrade flow evolves from immediate switch to billing-backed transition).
- Optional companion project (future Node service) for webhook fan-in if adopted.
- Tests across unit/integration.

**Schema changes**
- Add fields/tables for provider linkage:
  - `BillingCustomerId`, `BillingSubscriptionId`, `BillingStatus`,
  - optional `BillingEvents` table for idempotent event processing.
- Add unique constraints on provider IDs.

**API changes**
- `POST /api/plans/upgrade` may become asynchronous/intent-based:
  - returns pending state until billing confirmation.
- Add internal webhook endpoint(s) if handled in .NET:
  - `POST /api/billing/webhooks/{provider}` (signature verified).

**Services to add or modify**
- Add provider abstraction and one concrete adapter.
- Add idempotency and signature verification service.
- Update rate-limit entitlement source to trust active subscription state.

**Tests to add**
- Integration:
  - upgrade initiates billing flow,
  - webhook updates subscription,
  - duplicate webhook is idempotent,
  - invalid signature rejected.
- Unit:
  - event-to-domain mapping,
  - idempotency key handling.

**Risk level**
- **High** (external dependency, financial state, idempotency/security).

**Rollout order**
- **Phase 4** (after admin/RBAC foundation).

---

### 5) Background Processing for Subscription Lifecycle
**Goal**
- Move long-running and retryable subscription transitions out of request path for reliability (renewal checks, retries, dunning, plan sync).

**Affected files/projects**
- `Application/Services/` new background job orchestrators.
- `Presentation/Program.cs` host registration for background workers.
- Optional new worker project (if split host is preferred later).
- `Infrastructure/` persistence for job checkpoints/event state.
- Tests for job handlers.

**Schema changes**
- Add durable work/state tables (minimal):
  - `SubscriptionLifecycleJobs` or `OutboxEvents`.
- Add processing status fields (`Pending`, `Processing`, `Completed`, `Failed`, retry count).

**API changes**
- Usually none public initially.
- Internal/admin endpoint may be added for replay/retry observability.

**Services to add or modify**
- Add hosted services/job handlers for:
  - webhook reconciliation,
  - renewal state transitions,
  - failed payment state handling.
- Integrate with audit logging for significant lifecycle changes.

**Tests to add**
- Unit:
  - transition state machine logic,
  - retry/backoff rules.
- Integration:
  - simulated event processing updates subscription correctly,
  - idempotent reprocessing.

**Risk level**
- **Medium-High** (operational reliability and eventual consistency).

**Rollout order**
- **Phase 5** (after initial billing integration).

---

### 6) Observability Improvements (Logs/Metrics/Tracing)
**Goal**
- Improve production diagnostics for tenant traffic, auth failures, rate limiting, and billing/subscription workflows.

**Affected files/projects**
- `Presentation/Program.cs` (logging/metrics/tracing pipeline setup).
- Middleware files in `Presentation/Middleware/` for enriched telemetry.
- `Application/Services/*` for structured logs and correlation IDs.
- Deployment/runtime config files.

**Schema changes**
- None required for initial observability.
- Optional later: operational event table if persistent internal events are needed.

**API changes**
- None to functional API contracts.
- Optional secure diagnostics endpoints (health/details, metrics scraping) depending on hosting model.

**Services to add or modify**
- Add correlation/trace context helper.
- Add metric emitters for:
  - auth success/failure,
  - rate-limit blocks,
  - plan changes,
  - webhook processing outcomes.

**Tests to add**
- Unit/integration smoke checks that critical paths emit expected telemetry signals (without brittle payload matching).

**Risk level**
- **Medium** (low domain risk, moderate runtime/config complexity).

**Rollout order**
- **Phase 6 (parallelizable)**, but start foundational telemetry in Phase 1 and expand per feature.

---

## Recommended Rollout Sequence (Incremental Slices)
1. **Auth Sessions Foundation**: refresh token table + issue/refresh/revoke endpoints.
2. **Authorization Foundation**: RBAC schema + policy handlers + compatibility bridge from existing role string.
3. **Tenant Admin APIs**: user/tenant management endpoints behind RBAC.
4. **Billing Adapter MVP**: provider linkage + webhook ingestion + subscription state sync.
5. **Lifecycle Workers**: background reconciliation/retry/state transitions.
6. **Observability Hardening**: expand metrics/tracing dashboards and alerts across all previous phases.

## Cross-Cutting Guardrails for Every V2 Item
- Preserve tenant isolation explicitly in all queries and service methods.
- Require authorization for every new admin capability.
- Keep V1 endpoints backward compatible unless versioned replacement is introduced.
- Add integration tests for cross-tenant denial behavior for each new endpoint.
- Ship schema changes in small migrations with clear names per feature slice.
