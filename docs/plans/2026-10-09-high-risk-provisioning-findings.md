# Plan: remediate the High-risk tenant provisioning findings (1, 2, 3)

- **Date:** 2026-10-09
- **Branch:** `master` (each finding gets its own branch off up-to-date `master`; see Delivery)
- **Goal:** Close the three High-risk findings from the tenant provisioning security audit: host-based tenant resolution hijack, the shared auth rate-limit bucket behind NGINX, and email squatting through unverified registration.
- **Status:** planned
- **Approval:** approved in plan mode

## Context
The feature-auditor review of tenant provisioning (`POST /api/v1/auth/register`) rated the feature High risk because of three findings. None of them lets a tenant read another tenant's data. Each one lets an anonymous caller deny service to other tenants or users.

PR #136 (merged to `origin/master` as `1dd5c0e`) added registration input validation, including a reserved-subdomain list. That partly covers finding 1. This plan finishes the job on all three. Each finding ships as its own thin branch and PR, per AGENTS.md: tests and doc updates go in the same change.

Before starting, run `git pull` on local `master`. It is behind `origin/master`, which already has PR #136.

---

## Finding 1: a self-registered subdomain can hijack host-based tenant resolution

### Issue
`TenantMiddleware` (`Presentation/Middleware/TenantMiddleware.cs:42-51`) takes the first label of *any* dotted Host header and looks it up in `Tenants.Subdomain`. A match becomes the request's tenant hint, and the hint is compared with the JWT's `tenant_id` claim. A mismatch returns `403 TenantMismatch` (`:69-77`).

NGINX (`deploy/nginx/default.conf`) uses `server_name _` and forwards the client's Host header unchanged. So the platform's own hostname is subject to whatever tenant owns its first label. PR #136 now blocks new registrations of names like `api`/`www`/`app`, but gaps remain:
- Tenants that registered those names before #136 still hold them.
- A label not on the list still matches if the platform is served under it, for example `saas.example.com` and a tenant named `saas`.
- Host resolution runs on hosts that are not tenant hosts at all, such as `127.0.0.1` (label `127`) or internal service names.

### Repro case
Deployment: the API is served at `api.example.com`. Run this on a build without PR #136, or against a database where a tenant already owns `api`.
1. The attacker calls `POST /api/v1/auth/register` with `{ "companyName": "x", "subdomain": "api", "adminEmail": "a@evil.test", "adminPassword": "Passw0rd!" }` and gets 200.
2. A victim tenant's admin logs in and calls `GET https://api.example.com/api/v1/admin/tenant` with their JWT.
3. The middleware resolves the host label `api` to the attacker's tenant. That doesn't match the victim's `tenant_id`, so the response is `403 { "error": "TenantMismatch" }`. This happens to every tenant on that host.
4. Unauthenticated requests to that host resolve to the attacker's tenant and ignore `X-Tenant-ID`.

As an integration test: register a tenant with subdomain `api` by inserting it straight into the DB, since the validator now rejects it. Register a victim tenant. Send the victim's authenticated request with `Host: api.example.com` and assert the 403 (before the fix) or 200 (after).

### Steps
- [ ] Add a `TenantResolution` options section with a `BaseDomain` setting, for example `example.com`. Bind it in `Presentation/Program.cs` alongside the existing options bindings.
- [ ] In `TenantMiddleware`, only use host resolution when `Request.Host.Host` ends with `"." + BaseDomain` (ignoring case) and exactly one label sits before the base domain. Take that label lowercased.
  - In every other case skip strategy 1 and fall through to `X-Tenant-ID` and the JWT. This covers a different host, an IP address, `localhost`, more than one label, or an unset `BaseDomain`, where host resolution is disabled.
  - Optionally, also skip the label when `TenantRegistrationValidator` says it is reserved, as defence in depth. Expose a static `IsReservedSubdomain(string)` from `Application/Services/TenantRegistrationValidator.cs` rather than duplicating the list.
- [ ] Add `TenantResolution__BaseDomain=` to `deploy/env-examples/api.env.example` with a comment. Document it in `docs/operations.md` and update the tenant resolution paragraph in `docs/architecture.md`.
- [ ] One-off data check, documented in `docs/operations.md` and not automated: run a SQL query to list existing tenants whose subdomain is reserved or invalid, so an operator can decide how to rename them.

---

## Finding 2: the auth rate limiter collapses into one global bucket behind NGINX

### Issue
The `UnauthenticatedAuthEndpoints` policy (`Presentation/Program.cs:102-121`) partitions by `HttpContext.Connection.RemoteIpAddress`. That policy allows 10 requests per minute with no queue, and it covers register, login and refresh (`AuthController.cs:39,57,77`).

`Program.cs` never calls `UseForwardedHeaders`, and the env example doesn't set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. In the Compose profile every request reaches Kestrel from the NGINX container, so every client shares one bucket. One noisy client returns 429 to everyone, including token refreshes. The same proxy IP is also recorded in audit `IpAddress` and refresh-token `createdByIp`. Separately, partitioning by the full IPv6 address lets an attacker rotate through a /64.

### Repro case
Compose profile, with the API reachable only through `nginx` on port 80:
1. From client A, run `for i in $(seq 1 10); do curl -s -o /dev/null -w "%{http_code}\n" -X POST http://<vm>/api/v1/auth/login -H 'Content-Type: application/json' -d '{"email":"x@y.z","password":"wrong"}'; done`. All ten return `401`.
2. From a different machine B within the same minute, run `POST /api/v1/auth/refresh` with a valid refresh token. It returns `429`. B was never over its own limit.
3. Check `AuditLogs.IpAddress` for B's earlier login: it shows the NGINX container IP, not B's.

As an integration test: in `AuthRateLimitWebApplicationFactory`, set the connection's `RemoteIpAddress` to a fixed proxy IP with a startup filter. Send 10 logins with `X-Forwarded-For: 198.51.100.1`, then one with `X-Forwarded-For: 198.51.100.2`. Before the fix the second client gets 429; after it gets 401.

### Steps
- [ ] In `Program.cs`, configure `ForwardedHeadersOptions`:
  - `ForwardedHeaders = XForwardedFor | XForwardedProto`
  - `ForwardLimit = 1`
  - `KnownNetworks` / `KnownProxies` bound from a new `ForwardedHeaders` config section, holding a CIDR list and an IP list.
  - Call `app.UseForwardedHeaders()` first in the pipeline, before `UseHttpsRedirection` and `UseRateLimiter`.
  - With nothing configured, the defaults trust only loopback, so local `dotnet run` behaviour doesn't change.
- [ ] In `compose.yml`, give the default network a fixed subnet such as `172.28.0.0/24`. Set `ForwardedHeaders__KnownNetworks__0=172.28.0.0/24` in `api.env.example`. NGINX already sends `X-Forwarded-For $proxy_add_x_forwarded_for`. With `ForwardLimit = 1` only the entry NGINX appended is trusted, so a client-supplied XFF can't be spoofed.
- [ ] Change the limiter partition key so IPv6 addresses are grouped by their /64 prefix and IPv4 by the full address. Put this in a small helper next to `Presentation/RateLimiting/AuthRateLimitPolicyNames.cs`.
- [ ] Split `refresh` onto its own policy (`AuthRefreshEndpoint`) with a separate budget, so register and login floods can't block session renewal. Update the `AuthControllerBoundaryTests` theory that asserts the policy name per action.
- [ ] Docs: `docs/operations.md` covers the proxy trust configuration and the Compose subnet. `docs/architecture.md` covers the rate-limiting paragraph.

---

## Finding 3: global email uniqueness without verification lets anyone claim an email permanently

### Issue
`AuthOrchestrationService.RegisterAsync` creates the admin with `EmailVerifiedAt = now` and returns a JWT and refresh token immediately. `Users.Email` is globally unique (`ApplicationDbContext.cs:101`), and invite and add-user paths reject an existing email (`IdentityLifecycleService.cs:28-31,66-69`, `AdminTenantManagementController.cs:130-132`). Nothing in the product deletes a tenant or user.

So whoever registers an address first owns it on the whole platform, for good. They can log in as that address, and the real owner can never register, be invited, or be added.

### Repro case
1. The attacker calls `POST /api/v1/auth/register` with `{ "companyName": "x", "subdomain": "squat-1", "adminEmail": "ceo@victimcorp.com", "adminPassword": "Passw0rd!" }`. It returns 200 with tokens and the account is usable immediately.
2. The real owner calls `POST /api/v1/auth/register` with `adminEmail: "ceo@victimcorp.com"` and gets `400 { "error": "Email already registered" }`.
3. An admin of another tenant invites `ceo@victimcorp.com`. The response is `409`/`400` "User already exists".
4. The block never expires.

As an integration test: register twice with the same email under different subdomains and assert the second is rejected. After the fix, the first registration gets 202 with no tokens. Once its pending window has passed, simulated by back-dating `CreatedAt` in the DB, the second registration succeeds.

### Steps
- [ ] Add `TenantStatus.PendingVerification` to the **end** of the enum in `Domain/Entities/Tenant.cs`. Check whether the enum is stored as int or string; if a migration is needed, add one. The middleware, login and refresh already reject non-`Active` tenants (`TenantMiddleware.cs:109-118`, `AuthOrchestrationService` login and refresh), so a pending tenant is inert without further changes.
- [ ] In `RegisterAsync`:
  - Create the tenant as `PendingVerification` and the admin with `EmailVerifiedAt = null`.
  - In the same transaction, add a `UserVerificationToken` with a 48 h expiry, using the same hashing and token generation as `IdentityLifecycleService.AcceptInviteAsync` (`Application/Services/IdentityLifecycleService.cs:100-116`).
  - After the commit, call `IIdentityNotificationService.SendVerificationAsync`.
  - Return a new `RegisterAuthResult` shape with no `AuthResponse`: `{ tenantId, status: "pending_verification" }`. The controller maps it to `202 Accepted`.
  - Move the `TENANT_REGISTERED` audit write into the transaction while touching this code, which also addresses the post-commit failure path from audit finding 4.
  - To reuse the hashing and token generation, extract them into a small shared helper rather than copying them. Their current home is private statics in `IdentityLifecycleService`.
- [ ] Activation: in `IdentityLifecycleService.CompleteVerificationAsync`, after verifying the user, set the tenant to `Active` if it is `PendingVerification`. The client then logs in normally through `/auth/login`.
- [ ] Reclaim: in `RegisterAsync`, before the uniqueness checks, delete any `PendingVerification` tenant older than 48 h that holds the requested subdomain or the requested email. Tenant FKs cascade to its users, tokens and subscription; check the Subscription mapping at `ApplicationDbContext.cs:170-176`. This frees squatted names without a background job, and an attacker can hold an address for at most 48 h without access to its inbox.
- [ ] Optional: return the same `202` for "email already registered" and send the notice by email instead. This also closes enumeration finding 5. It isn't required for finding 3, so ship it only if wanted.
- [ ] Breaking change and docs:
  - Register no longer returns tokens.
  - Update `README.md`, the auth flow in `docs/architecture.md`, and `docs/contracts.md` if registration is listed there.
  - Add a `docs/CHANGELOG.md` line, since this changes a public contract.
  - `IdentityNotificationService` only logs today, so docs must say that verification email delivery is not live and operators must read the token from logs in local runs.

---

## Tests

### Finding 1
- **Integration** (`Tests/Integration/TenantLifecycleAndResolutionTests.cs`, which already has host-header tests at `:57-75`). Set `TenantResolution:BaseDomain=example.com` through `ApiWebApplicationFactory` configuration, then check:
  - `Host: <tenant>.example.com` still resolves the tenant.
  - The repro above returns 200 for the victim. The `api` tenant inserted directly is ignored, because `api` is reserved.
  - `Host: 127.0.0.1`, `Host: localhost`, and `Host: a.b.example.com` don't use host resolution.
  - With `BaseDomain` unset, host resolution is disabled.
- **Run:** `dotnet test --filter "FullyQualifiedName~TenantLifecycleAndResolutionTests"`

### Finding 2
- **Integration** (`Tests/Integration/AuthBruteForceRateLimitTests.cs`, extending `AuthRateLimitWebApplicationFactory` with a known proxy and a startup filter that fixes `RemoteIpAddress`):
  - Two different `X-Forwarded-For` clients get independent buckets.
  - A forged multi-hop XFF from the client can't escape its bucket.
  - Exhausting the login budget doesn't 429 `/auth/refresh`.
  - Existing 429 tests still pass.
- **Unit:** the partition-key helper groups IPv6 addresses by /64, handles IPv4, and handles a null address.
- **Unit:** `AuthControllerBoundaryTests` asserts that refresh uses the new policy name.
- **Run:** `dotnet test --filter "FullyQualifiedName~AuthBruteForceRateLimitTests|FullyQualifiedName~AuthControllerBoundaryTests"`

### Finding 3
- **Test infrastructure:** register a capturing `IIdentityNotificationService` in `ApiWebApplicationFactory` (the pattern is `RecordingIdentityNotificationService` in `Tests/UnitTests/IdentityLifecycleServiceTests.cs:195`). Update the shared helpers to register, complete verification with the captured token, then log in. The helpers are `SecurityTestHelpers.RegisterTenantAsync`, `ApiEndpointsTests.RegisterTenant`, and `TenantLifecycleAndResolutionTests.RegisterTenantWithSubdomainAsync`. Their signatures and return tuples don't change, so the many existing callers are unaffected.
- **Integration** (`ApiEndpointsTests`):
  - Register returns 202 with no token.
  - Login before verification returns the existing `EmailNotVerified` response.
  - Verify, then log in: 200.
  - A tenant-scoped request with a pending tenant's context gets 403.
  - Reclaim: a pending tenant with back-dated `CreatedAt` releases its email and subdomain to a new registration.
  - A non-expired pending registration still blocks.
  - Update `Register_ShouldCreateTenantAndReturnToken` to the new contract.
- **Tenant isolation** (`TenantIsolationSecurityTests`): a verification token for tenant A can't activate tenant B. `CompleteVerificationAsync` already filters by `TenantId`; assert it.
- **Unit:** `AuthControllerBoundaryTests` mapping for the 202 result.
- **Run:** `dotnet test` (full suite, because the shared helpers change).

### Verification (each PR)
- `dotnet restore && dotnet build --no-restore`: 0 errors.
- `dotnet test --no-build`: all pass, and report the counts.
- `git diff --check`.
- For any finding that adds a migration: `dotnet ef migrations has-pending-model-changes --project Infrastructure --startup-project Presentation` shows no drift.
- Manual check of finding 2 on the Compose profile, if the VM is available: rerun the curl repro from two hosts. Report it as not run if the VM isn't available.

---

## Delivery
1. Finding 2 first: config-only risk, smallest blast radius, and it fixes the global 429.
2. Finding 1: a middleware change plus config.
3. Finding 3 last: a breaking contract change and broad test-helper churn.

Each finding gets its own branch off up-to-date `master` (`fix/auth-rate-limit-forwarded-headers`, `fix/tenant-host-resolution-base-domain`, `fix/registration-email-verification`) and its own PR, with docs updated in the same change.

## Decisions & trade-offs
- **Finding 1: configured base domain (confirmed with the user).** Host resolution only applies to `<label>.<BaseDomain>`, and an unset `BaseDomain` disables it. This removes the dependency on the reserved list being complete. The trade-off is one new required setting for any deployment that relies on subdomain routing.
- **Finding 2: trust only configured proxies with `ForwardLimit = 1`.** This recovers the real client IP without letting clients spoof `X-Forwarded-For`. It requires a fixed Compose subnet. Refresh gets its own policy so login and register floods can't block session renewal.
- **Finding 3: no tokens until verified (confirmed with the user).** The admin must verify their email before logging in, and unverified registrations expire after 48 h. This is a breaking change for clients, and test helpers must complete verification. Expired pending registrations are reclaimed inline during registration rather than by a background job, which keeps the change thin.
- **Finding 3: `TenantStatus.PendingVerification` appended to the end of the enum.** Existing non-`Active` checks make a pending tenant inert with no extra enforcement code.

## Alternatives considered
- **Finding 1: rely only on the reserved-subdomain list from PR #136.** Rejected. It doesn't cover tenants that already hold reserved names, platform hosts not on the list, or IP and internal hosts.
- **Finding 3: keep returning tokens and purge unverified registrations after 48 h.** Rejected. It is non-breaking, but the squatter gets a working session during the 48 h window.

## Out of scope
- A distributed (Redis-backed) auth limiter for multi-replica deployments.
- A real email provider for `IdentityNotificationService`.
- Renaming existing tenants that hold reserved subdomains. This plan only produces the query to find them.
- Audit findings 4 to 7, except where noted in finding 3 (audit write moved into the transaction) and the optional step (enumeration).
