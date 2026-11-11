# Plan: operator tenant suspension and audit finding 7

- **Date:** 2026-10-13
- **Branch:** `fix/tenant-suspension` (off up-to-date `master`)
- **Goal:** Let operators suspend and reactivate a tenant through a signed internal endpoint, and make suspension complete by closing finding 7. Finding 7 is the gap that leaves authenticated `/auth/*` routes, and anonymous routes keyed by a `tenantId` in the body, open to suspended tenants.
- **Status:** done
- **Approval:** confirmed via /save-plan. The user chose the signed internal endpoint and manual suspension only.

## Context
`TenantStatus.Suspended` exists and is enforced by login, refresh and `TenantMiddleware`, but nothing in the product ever sets it. The only path today is a manual database update. Suspension is how an operator stops a non-paying, abusive or compromised tenant while keeping its data, so it is needed before self-serve production use.

Finding 7 (Medium) makes any suspension incomplete:
- **Logged-in `/auth/*` routes:** `TenantMiddleware` skips every `/api/vN/auth` path (`Presentation/Middleware/TenantMiddleware.cs:33`). So an access token issued before the suspension keeps working on invites, MFA, step-up and session routes until it expires.
- **Anonymous routes keyed by a body `tenantId`:** `invites/accept` and `password-reset/*` never check tenant status.

The API has no cross-tenant operator identity, and every user belongs to a tenant. The existing signed internal callback (`InternalBillingController` with `InternalRequestSignatureValidator`: HMAC-SHA256 over `timestamp\nbody`, a clock-skew window and a constant-time compare) is the pattern this plan reuses.

## Steps
- [x] **Reusable HMAC check.** Extract the core of `Application/Services/InternalRequestSignatureValidator.cs` into a static helper that takes the secret, skew, payload, timestamp and signature. The existing billing validator keeps its behaviour and calls the helper. Add a second validator for tenant operations that reads `TenantOperations:SharedSecret` and `TenantOperations:AllowedClockSkewMinutes`. An empty secret means every request is rejected, which disables the endpoint by default. Register it in `Presentation/Program.cs`.
- [x] **Suspension state on the tenant.** Add `StatusChangedAtUtc` (nullable) and `SuspensionReason` (max 500) to `Domain/Entities/Tenant.cs`. Add an EF migration. `TenantStatus` itself is unchanged.
- [x] **Tenant-wide refresh-token revocation.** Add `RevokeAllTenantTokensAsync(tenantId, reason)` to `IRefreshTokenService` and `RefreshTokenService`. It follows the per-user `RevokeAllActiveTokensAsync` loop but filters on the tenant only, so reactivation doesn't bring old sessions back.
- [x] **Signed operator endpoints.** Add `InternalTenantOperationsController`:
  - Routes: `POST /api/internal/tenants/{tenantId}/suspend` and `POST /api/internal/tenants/{tenantId}/reactivate`.
  - Body: `{ reason, actor }`. Headers: `X-Operations-Timestamp` and `X-Operations-Signature`, verified against the raw body the same way the billing callback is.
  - The orchestration lives in an application service (e.g. `TenantLifecycleService`), so the controller stays transport-only.
  - Transitions: suspend is allowed only from `Active`; reactivate only from `Suspended`. Anything else returns `409`. An unknown tenant returns `404` and a bad or missing signature `401`.
  - **Replay guard:** reject with `409` any request whose signed timestamp is not later than the tenant's `StatusChangedAtUtc`. That way a captured older request can't undo a newer change inside the skew window.
  - **Suspend:** set the status, `StatusChangedAtUtc` and `SuspensionReason`, revoke all of the tenant's refresh tokens, and audit `TENANT_SUSPENDED` with the reason and the caller-supplied actor, recorded as unauthenticated.
  - **Reactivate:** set `Active`, clear the reason, and audit `TENANT_REACTIVATED`.
  - Add `/api/internal/tenants` to the `TenantMiddleware` skip list next to `/api/internal/billing`.
- [x] **Finding 7, authenticated `/auth/*`.** In `TenantMiddleware`, when the path is an auth path and the request is authenticated with a `tenant_id` claim, load that tenant and require `Active`. Otherwise return the existing `403 TenantSuspended` response. Anonymous auth requests are unchanged.
- [x] **Finding 7, anonymous routes keyed by a body `tenantId`.** In `IdentityLifecycleService`:
  - `AcceptInviteAsync` returns null for a non-`Active` tenant.
  - `RequestPasswordResetAsync` silently does nothing.
  - `CompletePasswordResetAsync` returns false.
  - `RequestVerificationAsync` and `CompleteVerificationAsync` keep allowing `Active` and `PendingVerification`, but never a suspended tenant, and verification never changes a `Suspended` status.
  - The responses keep their existing generic messages.
- [x] **Network restriction.** In `deploy/nginx/default.conf`, deny `/api/internal/` from outside. Internal callers, the operator script and BillingService once its callback is wired, use the API on the Compose network (`api:8080`).
- [x] **Docs and config.**
  - `deploy/env-examples/api.env.example`: add `TenantOperations__SharedSecret` and `TenantOperations__AllowedClockSkewMinutes`.
  - `docs/contracts.md`: document the internal tenant-operations contract: routes, signing, transitions, status codes and the replay guard.
  - `docs/operations.md`: how to suspend and reactivate with a signed `curl` and `openssl` example from inside the Compose network, plus the config rows and the NGINX restriction.
  - `docs/architecture.md`: the tenant lifecycle, and the closed `/auth` gap.
  - `docs/CHANGELOG.md`: an Unreleased entry.

## Tests
- **Unit:** the HMAC helper accepts a valid signature and rejects a wrong secret, an empty secret, a timestamp outside the skew window, and a missing header value.
- **Integration** (new `Tests/Integration/TenantSuspensionTests.cs`; `ApiWebApplicationFactory` gets a test `TenantOperations:SharedSecret`):
  - A missing or invalid signature returns `401` and leaves the tenant unchanged. With the secret unset, every request returns `401`.
  - Suspend: `200`, the status is `Suspended` with the reason, all of the tenant's refresh tokens are revoked, and a `TENANT_SUSPENDED` audit entry exists.
  - With an access token issued before the suspension, `GET /auth/sessions`, `POST /auth/invites` and `POST /auth/mfa/enroll/initiate` return `403` (finding 7), and so does `GET /admin/tenant`. Login returns `403` and refresh returns `401`.
  - With a valid invite token, `invites/accept` for a suspended tenant returns `400` and creates no user. A password-reset request issues no token, and completing a reset fails.
  - Reactivate: `200` and status `Active`. Login works again, and refresh tokens from before the suspension stay revoked.
  - Invalid transitions return `409`: suspending a suspended tenant, reactivating an active tenant, and suspending a `PendingVerification` tenant. An unknown tenant returns `404`.
  - Replay guard: an earlier signed reactivate request, re-sent after a later suspension, returns `409`, and the tenant stays suspended.
- **Tenant isolation** (`Tests/Integration/TenantIsolationSecurityTests.cs`): suspending tenant A leaves tenant B's access and refresh tokens working.
- **Regression:** the existing `InternalBillingSecurityTests` and `CrossServiceBillingContractTests` still pass after the validator refactor. The existing pending-verification tests still pass, because verification still works for `PendingVerification`.
- **Run:** `dotnet test` (full suite).

### Verification
- `dotnet restore && dotnet build --no-restore`: 0 errors.
- `dotnet test --no-build`: all pass; report the counts.
- `dotnet ef migrations has-pending-model-changes --project Infrastructure --startup-project Presentation`: no drift after the new migration.
- `git diff --check`.
- Manual check of the NGINX `deny` on the Compose profile, if the VM is available: `/api/internal/*` from outside returns `403`, and works from inside the network. Report it as not run if the VM isn't available.

## Decisions & trade-offs
- **Signed internal endpoint rather than a platform-admin role** (chosen by the user). It reuses the existing HMAC pattern and doesn't break the one-tenant-per-identity model (JWT `tenant_id`, `TenantMismatch`, tenant query filters, tenant-scoped RBAC). The trade-off is no per-person accountability: the audit's `actor` is self-reported by whoever holds the secret.
- **Separate secret from billing.** If the billing secret leaked, it couldn't be used to suspend tenants, and the two can be rotated independently.
- **Replay guard from `StatusChangedAtUtc`** rather than a request-id table. One column on the tenant, and it gives last-writer-wins ordering for a rare operator action.
- **Revoke refresh tokens on suspend.** Refresh already rejects non-`Active` tenants; revocation means reactivation starts from clean sessions. Access tokens need no revocation, because once finding 7 is closed the middleware checks the tenant's status on every request.
- **Manual only** (chosen by the user). BillingService callbacks don't reach the API at runtime, and suspending for non-payment would need `/billing` left reachable so the tenant can pay. How billing should affect access is deferred.

## Alternatives considered
- **Platform-admin role:** rejected for now. It is a cross-tenant identity that would need middleware exceptions, query-filter bypasses and new login rules: a large new security surface. Revisit when a real back office is needed.
- **Operator CLI against the database:** rejected. It needs host access and a new entry point, and gets no HTTP signature or audit path for free.
- **Automatic suspension on subscription expiry:** deferred, see Decisions.

## Out of scope
- How billing state affects access (`Expired`/`Canceled` → suspend or downgrade).
- The `Deleted` status, data export or retention, and tenant deletion.
- Revoking pending invites or step-up sessions on suspend. Both are blocked while the tenant is suspended, and step-up sessions expire after 10 minutes.
- A back-office UI or operator identities.

## Changes
- **2026-10-13 — Implemented** on `fix/tenant-suspension`. No change to the approach. Details:
  - The helper is `Application/Services/HmacRequestSignature.cs`, with `InternalRequestSignatureValidator` (billing) and the new `TenantOperationsSignatureValidator` on top of it. Status changes live in `TenantLifecycleService`; the endpoints are in `InternalTenantOperationsController`. Migration: `AddTenantSuspensionState`.
  - `StatusChangedAtUtc` stores the *signed* request timestamp, not the server time, so status changes are ordered by when the operator signed them.
  - Suspend requires a non-empty `reason`, returning `400` otherwise. This adds a test, `Suspend_WithoutReason_ShouldReturn400`, that the plan didn't list.
  - `/api/internal/tenants` was added to the skip list in `RateLimitMiddleware` as well as in `TenantMiddleware`; the plan only named `TenantMiddleware`.
  - The NGINX rule is a case-insensitive regex (`~* ^/api/internal(/|$)`) because ASP.NET routing ignores case. I checked it with `nginx -t` in a throwaway `nginx:1.27-alpine` container and probed it with curl: `/api/internal/...`, `/API/Internal/...`, `//` and percent-encoded variants all get `403`, and other paths pass through. The Compose VM check was not run, because no VM was available.
  - The signed-request example in `docs/operations.md` was checked: its `openssl` signature matches a reference HMAC, and the NGINX image's busybox `wget` supports the flags it uses.
  - Mutation checks: removing the `/auth` tenant check, the replay guard, or the anonymous-flow checks each makes exactly its corresponding test fail.
  - Found on the way, not changed (out of scope): the billing section of `docs/contracts.md` describes signing as `<timestamp>.<body>` with a Unix-seconds timestamp. The API actually verifies `<timestamp>\n<body>` with an ISO timestamp, and BillingService doesn't sign callbacks yet.
