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

For tenant-sensitive requests, `TenantMiddleware` can resolve a tenant hint from the request subdomain or `X-Tenant-ID`. When a JWT is authenticated, its `tenant_id` claim is authoritative. A conflicting hint is rejected, and the resolved tenant must exist and be active before scoped tenant context is set.

Tenant isolation is also enforced through tenant predicates in data access and service-layer checks. Sensitive regression suites cover authentication, administration, billing, analytics, audit, entitlements, internal callbacks, and webhook endpoint management. These controls reduce cross-tenant risk but do not replace code review for every new query and endpoint.

## Authentication and authorization

The API uses JWT Bearer authentication with issuer, audience, lifetime, and symmetric signing-key validation. Implemented identity capabilities include registration, login, refresh-token rotation/revocation, invites, email verification, password reset, MFA enrollment/verification, and step-up sessions.

Tenant-scoped RBAC uses roles, permissions, policy handlers, and service-layer authorization. Sensitive controllers combine authenticated identity, tenant context, RBAC, and entitlement checks as applicable.

Unauthenticated auth routes use a fixed-window per-IP limiter. Authenticated tenant traffic also passes through a plan-aware Redis-backed limiter. The two controls are independent.

## Plans, subscriptions, and entitlements

The API owns the plan catalog and internal subscription lifecycle. Entitlements are assembled from plan mappings, active add-ons, and tenant overrides with deterministic precedence. Current enforcement covers selected billing reads and mutations, plan upgrades, advanced tenant administration, and audit-log analytics; enforcement is not claimed for every possible API surface.

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

Endpoint lookup and management are tenant-scoped. Delivery identifiers and source-event keys provide replay and duplicate protection. Rotation initiation issues pending secret material; active cutover/finalization remains a future operational phase.

## Usage analytics and audit

Audit entries and usage analytics belong to the API and are queried in tenant scope. Analytics is an internal aggregation/query foundation rather than a separate warehouse. Access is protected by authorization and entitlements where applicable.

## Persistence and background processing

- PostgreSQL stores authoritative API state and outbound-webhook workflow records.
- Redis stores plan-aware request counters; it is not a general platform cache.
- `OutboundWebhookDeliveryDispatcher` is an ASP.NET Core hosted service.
- BillingService stores queue, retry, deduplication, and dead-letter state in a JSON file. This is appropriate for deterministic local and single-instance validation, not distributed coordination.

## Observability

Both services emit structured correlation-aware logs and expose JSON health and in-memory metrics endpoints. The API health endpoint checks self and PostgreSQL; it does not check Redis. BillingService health does not verify provider connectivity, callback delivery, or live reconciliation sources.

No deployed OpenTelemetry exporter, durable metrics backend, dashboard, alerting system, or SLO process is part of the current implementation. See [operations.md](operations.md) for endpoint semantics and [decisions.md](decisions.md) for the deferred production concerns.
