# Changelog

This is a milestone-level implementation history, not a release promise or active backlog. Exact historical plans and completion notes are retained in [the archive](archive/README.md).

## Unreleased

- Corrected the documented internal billing callback signing format in `docs/contracts.md` to match what the API verifies: an ISO 8601 timestamp, a signing input of `<timestamp>\n<body>`, and a `sha256=`-prefixed signature. Added a published known-answer test vector. API behaviour is unchanged.
- Operator tenant suspension: signed `POST /api/internal/tenants/{id}/suspend` and `/reactivate`, keyed by a new `TenantOperations:SharedSecret`. Suspending revokes the tenant's refresh tokens and is audited. A suspended tenant is now also blocked on authenticated `/auth/*` routes and on invite acceptance and password reset. NGINX now rejects `/api/internal/*` from outside the Compose network. New migration `AddTenantSuspensionState`.
- `POST /api/v1/auth/register` no longer returns `400 "Email already registered"`. A known email gets the same `202` as a new registration, creates nothing, and triggers an existing-account notice. Login with an unknown email now costs the same as a wrong password. The invite and add-user duplicate-email errors now read `"Email is already in use"`.
- **Breaking:** `POST /api/v1/auth/register` now returns `202 Accepted` with `{ tenantId, status, verificationExpiresAt }` instead of `200` with tokens. The tenant stays `PendingVerification` until the admin completes `/api/v1/auth/verification/complete`, then logs in through `/api/v1/auth/login`. Unverified registrations release their subdomain and email after 48 hours.

## V4 — pre-deployment engineering maturity

Implemented local engineering maturity includes:

- deterministic `bootstrap`, `reset`, `seed`, `run`, `smoke`, and `test` workflows;
- .NET/BillingService billing contract conformance tests;
- fixture-driven replay, duplicate, out-of-order, stale, and invalid-signature billing tests;
- tenant-isolation and authorization regression coverage across sensitive surfaces;
- expanded entitlement precedence and lifecycle matrix tests;
- local health, metrics, correlation, diagnostic-field, and sensitive-data quality gates;
- extraction of authentication orchestration from `AuthController` into an application service;
- request tenant-resolution caching and entitlement query batching;
- explicit auth endpoint throttling and explicit local CORS policy;
- outbound-webhook endpoint management and signing-secret rotation initiation;
- Docker Compose and NGINX local Ubuntu VM profile; and
- consolidated durable documentation with historical planning artifacts archived.

Live provider runtime wiring, production telemetry, hardened public CORS, deployed recovery procedures, and deployment topology remain outside the completed scope.

## V3 — platform capability foundations

V3 established entitlements and progressive enforcement, tenant usage analytics, outbound tenant-webhook infrastructure, BillingService durable workflow/reconciliation foundations, identity lifecycle hardening, and associated isolation/security tests. Some V3 observability and provider-integration documents were designs rather than completed production runtime features.

## V2 — identity, authorization, administration, and billing foundations

V2 added refresh-token rotation and revocation, tenant-scoped RBAC and permissions, tenant administration, provider-ready internal billing boundaries, background workflow foundations, and broader security and operations coverage.

## V1 — core multi-tenant API

V1 established the layered ASP.NET Core solution, tenant registration and login, tenant resolution, PostgreSQL persistence, plan/subscription foundations, Redis-backed plan limits, audit logging, health checks, and initial automated tests.
