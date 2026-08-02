# V4 AuthController Application-Layer Boundary Design (Pre-Implementation)

Date: April 26, 2026  
Scope: `Presentation/Controllers/AuthController.cs` orchestration refactor design only (no runtime behavior change yet).

## Goals and non-goals

### Goal
Move registration, login, and refresh-flow orchestration out of `AuthController` and behind application-service interfaces so the controller no longer depends directly on `ApplicationDbContext` for those paths.

### Non-goals
- No HTTP contract change (`AuthResponse` and existing error payload shapes remain as-is where practical).
- No controller-wide rewrite in this slice.
- No ownership shifts that violate existing architecture boundaries (.NET API remains system of record; BillingService remains provider-facing).

## 1) Proposed service responsibilities

## Controller (`AuthController`)
- Keep HTTP-specific concerns only:
  - route binding and request-model validation
  - status-code selection and response shaping
  - claim/header extraction (`tenant_id`, `sub`, step-up header)
  - remote-IP capture from `HttpContext`
- Call orchestration methods on application service(s) and map service outcomes to existing response contracts.
- Keep endpoints unrelated to this slice unchanged.

## Application service (`IAuthOrchestrationService`, new)
- Own orchestration/business flow for register/login/refresh/revoke and shared auth-context checks currently implemented with direct `DbContext` queries.
- Coordinate existing collaborators (`IJwtService`, `IRefreshTokenService`, `IAuditService`, `ITenantContext`, `IIdentityLifecycleService`, `IMfaService`) without introducing HTTP abstractions.
- Enforce tenant-safe and identity lifecycle checks currently in controller logic:
  - duplicate subdomain/email checks on register
  - credential + tenant status + verification + MFA gate checks on login
  - refresh token + tenant/user context validation on refresh
  - step-up validation (for paths that currently require it)
- Return typed operation results (success/failure code + data) so controller can preserve current status codes and payloads.

## Infrastructure/data layer
- Provide data-access abstractions used by the orchestration service instead of controller `DbContext` usage.
- Minimal repository/query boundary for this refactor:
  - user lookups with tenant joins
  - registration write transaction (tenant/user/subscription create)
  - last-login update
  - refresh-context user lookup
  - MFA enrollment challenge + step-up session reads/writes used by auth paths
- Keep EF Core implementation details in Infrastructure.

## 2) Proposed new/updated service methods

## New interface: `Domain/Interfaces/IAuthOrchestrationService.cs`

Recommended minimal methods:

1. `Task<RegisterResult> RegisterAsync(RegisterAuthCommand command, CancellationToken cancellationToken = default)`
   - Input includes company/subdomain/admin email/password and request IP.
   - Performs duplicate checks + tenant/user/subscription creation transaction.
   - Issues access + refresh tokens and returns `AuthResponse` payload data.

2. `Task<LoginResult> LoginAsync(LoginAuthCommand command, CancellationToken cancellationToken = default)`
   - Input includes email/password and request IP.
   - Performs credential/tenant/verification/MFA checks.
   - Updates last login, audits event, issues tokens.

3. `Task<RefreshResult> RefreshAsync(RefreshAuthCommand command, CancellationToken cancellationToken = default)`
   - Input includes tenantId/refreshToken/request IP.
   - Validates active token, tenant-bound user context, tenant status, rotates token, audits event.

4. `Task<RevokeRefreshTokenResult> RevokeRefreshTokenAsync(RevokeRefreshTokenCommand command, CancellationToken cancellationToken = default)`
   - Shared logic for logout/revoke endpoint parity.

5. `Task<StepUpValidationResult> ValidateStepUpAsync(StepUpValidationCommand command, CancellationToken cancellationToken = default)`
   - Used by existing sensitive endpoints that currently query `User` + `UserStepUpSession` directly.

### Result-shape guidance (smallest safe)
- Use operation-specific `Result` types with:
  - `bool Succeeded`
  - `AuthErrorCode Error` (enum for stable mapping)
  - `AuthResponse? Response` for token-returning flows
  - small supplemental fields where needed (e.g., `RequiresMfa`, `RevokedCount`)
- Keep string error messages centralized in controller mapping to preserve existing API response text where practical.

### Supporting data abstractions (new, minimal)

- `IAuthUserReadRepository`
  - `GetByEmailWithTenantAsync(email)`
  - `GetByIdWithTenantAsync(tenantId, userId)`
  - `UpdateLastLoginAsync(tenantId, userId, loginAtUtc)`

- `IAuthRegistrationRepository`
  - `SubdomainExistsAsync(subdomain)`
  - `EmailExistsAsync(email)`
  - `CreateTenantAdminAndStarterSubscriptionAsync(...)` (single transaction boundary)

- `IMfaChallengeRepository` (only if needed by moved step-up paths in this slice)
  - enrollment challenge create/find/consume
  - step-up session create/find

These abstractions should be implemented in Infrastructure with EF Core.

## 3) Controller responsibilities after refactor

For `register`, `login`, `refresh` (and optional revoke/step-up helper paths included in same slice):

- Keep request validation and exact `BadRequest` short-circuit checks already present.
- Build command DTOs from request + `HttpContext` values.
- Call `IAuthOrchestrationService`.
- Map typed result to existing HTTP outcomes:
  - `BadRequest` for required-field/invalid-flow failures that are currently 400
  - `Unauthorized` for invalid credentials/token context failures that are currently 401
  - `StatusCode(403, ...)` where currently used for tenant suspended / step-up required paths
  - `Ok(AuthResponse)` on success
- Keep endpoints that already delegate to `IIdentityLifecycleService` and `IRefreshTokenService` unchanged unless they currently require direct context lookups in shared helper paths.

## 4) Exact files to change first

Ordered thin-slice sequence:

1. `Domain/Interfaces/IAuthOrchestrationService.cs` (new)
2. `Domain/Outputs/Auth/` result + command DTO files (new, minimal set)
3. `Application/Services/AuthOrchestrationService.cs` (new)
4. `Infrastructure/Data/Repositories/Auth/` EF-backed repository implementation files (new)
5. `Presentation/Controllers/AuthController.cs` (constructor dependency + register/login/refresh/revoke helper rewiring)
6. `Presentation/Program.cs` (DI registrations)
7. `Tests/Integration/AuthenticationSecurityTests.cs` (assert no contract/status regressions)
8. `Tests/Integration/IdentityLifecycleSecurityTests.cs` or related auth tests that cover refresh/login/register paths
9. `README.md` and `docs/V4-Implementation-Backlog.md` (iteration notes)

## 5) Migration strategy from current controller logic

## Phase 0: design + invariants (this iteration)
- Lock response-contract expectations from current integration tests.
- Document exact status-code/error-body mapping table for register/login/refresh/revoke.

## Phase 1: register flow extraction
- Move duplicate-check + create-transaction + token issue orchestration to service.
- Keep controller response mapping unchanged.
- Run auth integration tests.

## Phase 2: login flow extraction
- Move credential + tenant status + verification + MFA-gate + last-login update to service.
- Preserve `requiresMfa` unauthorized payload behavior.

## Phase 3: refresh flow extraction
- Move active-token lookup + user/tenant lookup + rotate + audit to service.
- Preserve tenant-context safety and existing unauthorized message contracts.

## Phase 4: shared revoke/step-up helpers
- Extract `RevokeRefreshTokenInternal` and `ValidateStepUpOrForbidAsync` lookups as needed to remove remaining direct `DbContext` calls in auth paths.
- Keep step-up header and policy handling in controller (HTTP concern).

## Phase 5: hardening and cleanup
- Verify `AuthController` has no direct `ApplicationDbContext` dependency for targeted flows.
- Keep unrelated endpoints unchanged in this refactor.
- Update docs and backlog with completed implementation slice status.

## Risk controls
- Preserve tenant context setting before tenant-scoped service calls.
- Keep audit event names unchanged.
- Avoid broad renaming/restructuring; use additive interfaces and incremental endpoint rewiring.
- Add regression assertions for exact status/message compatibility on register/login/refresh failure modes.
