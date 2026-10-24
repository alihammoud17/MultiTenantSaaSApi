# Architecture

## System context and ownership

The platform has two runtime services with deliberately different ownership:

- The **ASP.NET Core API** is the system of record for tenants, users, identity state, RBAC, tenant business data, plans, entitlements, audit data, and internal subscription state.
- **BillingService** is the provider-facing companion. It owns provider adapters, webhook verification/normalization, retry-safe provider workflows, and reconciliation logic. It must validate external identifiers against internal mappings and must not become a second authority for tenant access or unrelated business data.

The services communicate through the signed, versioned internal billing contract described in [contracts.md](contracts.md). The current default BillingService runtime does not dispatch that contract to the API; contract behavior is exercised deterministically in tests.

## .NET solution layers

- `Presentation` hosts controllers, middleware, JWT and authorization wiring, CORS, rate limiting, Swagger, health, metrics, and the outbound-webhook dispatcher.
- `Application` implements authentication orchestration, refresh tokens, identity lifecycle, MFA, RBAC, auditing, rate limiting, billing callbacks, entitlements, analytics, and outbound-webhook management.
- `Domain` contains entities, DTOs, service interfaces, responses, entitlement keys, and authorization constants.
- `Infrastructure` owns EF Core persistence, model mappings, PostgreSQL migrations, and tenant-context support.
- `Tests` contains unit and integration coverage.

Controllers should remain transport-focused; business orchestration belongs in application services. `AuthOrchestrationService`, for example, owns register, login, and refresh orchestration that was historically controller-based.

## Tenant resolution and isolation

For tenant-sensitive requests, `TenantMiddleware` can resolve a tenant hint from the request subdomain or `X-Tenant-ID`. Subdomain resolution only applies to hosts of the form `<subdomain>.<TenantResolution:BaseDomain>`: any other host (a different domain, deeper subdomains, IP addresses, `localhost`) and any reserved label such as `api` or `www` is ignored, and host resolution is disabled when no base domain is configured. When a JWT is authenticated, its `tenant_id` claim is authoritative. A conflicting hint is rejected, and the resolved tenant must exist and be active before scoped tenant context is set.

Authenticated `/auth/*` routes skip tenant resolution, because register, login and refresh run before there is a tenant context. They still require the caller's JWT tenant to exist and be `Active`, so a token issued before a suspension stops working everywhere at once. Anonymous identity routes that take a `tenantId` in the body also require an `Active` tenant: invite acceptance and password reset. Email verification also accepts `PendingVerification`, and never changes a `Suspended` status.

Operators suspend and reactivate tenants through the signed internal endpoints `POST /api/internal/tenants/{id}/suspend|reactivate` (`InternalTenantOperationsController` → `TenantLifecycleService`). See [contracts.md](contracts.md). Suspension is manual: billing state (`Expired`/`Canceled` subscriptions) does not suspend a tenant automatically.

Tenant isolation is also enforced through tenant predicates in data access and service-layer checks. Sensitive regression suites cover authentication, administration, billing, analytics, audit, entitlements, internal callbacks, and webhook endpoint management. These controls reduce cross-tenant risk but do not replace code review for every new query and endpoint.

## Authentication and authorization

The API uses JWT Bearer authentication with issuer, audience, lifetime, and symmetric signing-key validation. Implemented identity capabilities include registration, login, refresh-token rotation/revocation, invites, email verification, password reset, MFA enrollment/verification, and step-up sessions. The `expiresAt` returned by login and refresh is the access token's own `exp`, so it follows `Jwt:ExpirationMinutes`. Audit entries for the anonymous auth flows (`TENANT_REGISTERED`, `USER_LOGGED_IN`, `USER_TOKEN_REFRESHED`, `USER_INVITE_ACCEPTED`) record the acting user as `UserId`; operator tenant-status entries have no user and keep an empty `UserId`.

Tenant registration (`POST /api/v1/auth/register`) validates and normalizes input in `TenantRegistrationValidator` before any database access. Company name, subdomain, and email are trimmed. The subdomain is lowercased and must be a 3–50 character DNS label (lowercase letters, digits, and inner hyphens), not entirely numeric, and not on the reserved list (for example `www`, `api`, `admin`), because `TenantMiddleware` resolves tenants from the first host label. Email must be a plain address of at most 255 characters, and the admin password must be 8 characters to 72 UTF-8 bytes (the BCrypt input limit). Invalid input returns `400` with a specific `error` message. Emails are stored in canonical lowercase form (`Domain/Identity/EmailAddress.Normalize`) everywhere they are written or looked up: registration, login, invites, verification, password reset and admin add-user. So login ignores case, and the case-sensitive unique indexes on `Users.Email` also enforce case-insensitive uniqueness. The subdomain uniqueness check stays case-insensitive so rows stored before normalization still count as duplicates. If two simultaneous registrations both pass the checks, the database index rejects the second insert; the checks are then re-run so the loser gets the normal answer (`400 "Subdomain already taken"`, or the existing-account `202`) instead of a `500`.

A successful registration returns `202 Accepted` with `{ tenantId, status: "pending_verification", verificationExpiresAt }` and no tokens. The tenant is created as `PendingVerification` with an unverified admin, a 48-hour verification token is sent through `IIdentityNotificationService`, and the `TENANT_REGISTERED` audit entry is written in the same transaction. A pending tenant is inert: login returns `401` "Email is not verified" and tenant-scoped requests are rejected by `TenantMiddleware`. Completing `POST /api/v1/auth/verification/complete` verifies the admin and sets the tenant `Active`; the client then logs in through `/auth/login`. A pending registration older than 48 hours is deleted (with its users, tokens, and subscription) when a new registration asks for the same subdomain or email, so an unverified sign-up cannot hold either one permanently. Verified tenants are never reclaimed.

Registration does not reveal whether an email has an account. The subdomain is checked first, and a taken subdomain returns `400 "Subdomain already taken"` regardless of the email. If the subdomain is free but the email already belongs to a user, the response is the same `202` shape with a tenant id that does not exist. Nothing is written, the password is still hashed so the timing is similar, and the existing owner gets an "account already exists" notice through `IIdentityNotificationService`. Login verifies against a dummy BCrypt hash when the email is unknown, so an unknown email costs the same as a wrong password and returns the same `401`. Remaining signals, accepted by design:
- Tenant admins still see `400 "Email is already in use"` from invites and add-user. Those callers are authenticated, RBAC-gated, audited and rate-limited.
- A prober can reuse the subdomain from a `202` with a different email. A taken subdomain shows the first registration was real. Each probe costs two requests from the shared register and login budget.

Tenant-scoped RBAC uses roles, permissions, policy handlers, and service-layer authorization. Sensitive controllers combine authenticated identity, tenant context, RBAC, and entitlement checks as applicable.

Unauthenticated auth routes use in-process fixed-window per-client limiters: register and login share a budget of 10 requests per minute, and refresh has its own budget of 30 per minute so login or register floods cannot block session renewal. The anonymous account-recovery routes (`verification/request`, `verification/complete`, `password-reset/request`, `password-reset/complete`, `invites/accept`) share a separate budget of 10 per minute, which limits both mail flooding and token guessing. Clients are keyed by IPv4 address or IPv6 /64 prefix. Behind a reverse proxy, the client address comes from `X-Forwarded-For` only when the proxy is listed in `ForwardedHeaders:KnownNetworks`/`KnownProxies` (see `docs/operations.md`). MFA code checks (`mfa/enroll/verify` and `mfa/step-up`) share a budget of 5 attempts per 5 minutes per user, keyed by the JWT's tenant and subject rather than the client address, so a stolen access token cannot be used to enumerate TOTP codes. The rate limiter runs after authentication so that policy can see the caller. There is no account lockout. Authenticated tenant traffic also passes through a plan-aware Redis-backed monthly limiter, which counts with an atomic increment so concurrent requests cannot exceed the plan limit, and fails open when Redis is unavailable. The controls are independent.

## Plans, subscriptions, and entitlements

The API owns the plan catalog and internal subscription lifecycle. Entitlements are assembled from plan mappings, active add-ons, and tenant overrides with deterministic precedence. Current enforcement covers selected billing reads and mutations, plan upgrades, advanced tenant administration, tenant CORS origin management, and audit-log analytics; enforcement is not claimed for every possible API surface.

Evaluating an entitlement takes three queries (subscription with plan values and overrides, definitions, add-on contributions), whether for one key or several (`EvaluateManyAsync`). Within an HTTP request, each tenant and key is evaluated once and the result is reused, so a change made later in the same request is not seen; outside a request nothing is cached.

Subscription state changes can originate from tenant actions or the authenticated internal billing callback. Provider state never directly becomes authoritative without tenant/subscription mapping validation.

## BillingService

BillingService is a Node.js/TypeScript HTTP service with:

- `GET /health` and `GET /metrics` diagnostics;
- `POST /webhooks/provider` provider ingress;
- normalized internal subscription-event types;
- a file-backed workflow queue with event-id deduplication, retries, exponential backoff, dead-letter state, and restart recovery;
- a reconciliation algorithm that classifies provider/internal drift and enqueues deterministic correction intents;
- a Stripe HTTP gateway slice for checkout sessions, billing-portal sessions, and tenant-validated invoice synchronization.

The default runtime is intentionally incomplete: all configured provider names select the placeholder webhook adapter, the callback publisher logs rather than calling the API, and the scheduled reconciliation sources return empty lists. The Stripe gateway and reconciliation behavior are testable components, not a live end-to-end billing control plane.

## Outbound tenant webhooks

The API owns outbound tenant-event publication. Tenant-scoped endpoint management supports creation, listing, update, activation/deactivation, deletion, and signing-secret rotation initiation. Events and deliveries are persisted, delivery requests are signed, and a hosted dispatcher applies retry and terminal-status behavior.

Endpoint lookup and management are tenant-scoped. Callback URLs must be absolute `http` or `https` URLs, field lengths are checked against their columns (`400` otherwise), and endpoint names are unique per tenant (`409`). The dispatcher fails a delivery whose URL cannot be used without affecting the rest of the batch, and stores at most 1000 characters of the last error. Delivery identifiers and source-event keys provide replay and duplicate protection. Rotation initiation issues pending secret material; active cutover/finalization remains a future operational phase.

## Usage analytics and audit

Audit entries and usage analytics belong to the API and are queried in tenant scope. Analytics is an internal aggregation/query foundation rather than a separate warehouse. Access is protected by authorization and entitlements where applicable.

## Persistence and background processing

- PostgreSQL stores authoritative API state and outbound-webhook workflow records.
- Redis stores plan-aware request counters and caches the tenant-registered CORS origin lists (invalidated on change, 5-minute expiry); it is not a general platform cache.
- `OutboundWebhookDeliveryDispatcher` is an ASP.NET Core hosted service.
- BillingService stores queue, retry, deduplication, and dead-letter state in a JSON file. This is appropriate for deterministic local and single-instance validation, not distributed coordination.

## Observability

Both services emit structured correlation-aware logs and expose JSON health and in-memory metrics endpoints. The API health endpoint checks self and PostgreSQL; it does not check Redis. BillingService health does not verify provider connectivity, callback delivery, or live reconciliation sources.

No deployed OpenTelemetry exporter, durable metrics backend, dashboard, alerting system, or SLO process is part of the current implementation. See [operations.md](operations.md) for endpoint semantics and [decisions.md](decisions.md) for the deferred production concerns.
