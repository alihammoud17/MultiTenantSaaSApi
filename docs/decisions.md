# Decisions and Open Review Items

This file records durable decisions and explicitly unresolved operational choices. Historical proposals are retained under [archive](archive/README.md).

## Accepted decisions

### The .NET API is the system of record

Tenant identity, users, RBAC, business data, plans, entitlements, audit data, and internal subscription state belong to the API and PostgreSQL. BillingService may mirror provider-facing identifiers but does not independently authorize tenant access or own unrelated domain state.

### BillingService isolates provider concerns

Provider adapters, provider webhook verification, normalization, retry-safe provider work, and reconciliation belong in BillingService. Provider-specific payloads and status names must be normalized before crossing the internal contract.

### Contracts are authenticated and versioned

Internal billing callbacks use a versioned DTO and timestamped HMAC over the raw body. Contract changes require coordinated producer and consumer tests. Tenant and subscription mapping is validated internally rather than trusted from provider input.

### Public APIs use URL-segment versions

Public API routes use `/api/v1/...`. A new major API version is reserved for breaking contract changes. Backward-compatible additions do not require a new major version. Operational endpoints such as `/health` and `/metrics` are not public business API versions.

Application releases should use Semantic Versioning. Branch names are workflow conventions; release tags, not branch names, identify releases.

### Local validation is the present maturity target

The current target is deterministic, production-like local validation before deployment. “Implemented” means code and automated local tests exist; it does not imply production certification, deployment exercise, load validation, or on-call readiness.

### Secrets stay outside the repository

No real credentials belong in Git. Local user secrets or environment variables are used for development. The Compose profile expects runtime env files under `/etc/multitenant-saas-api/`; checked-in examples contain placeholders only.

### Current billing durability is file-backed

BillingService uses a JSON workflow state file for deterministic retry, deduplication, dead-letter, and restart-recovery tests. This is not a distributed queue and assumes controlled single-instance file access.

### Current observability is local JSON and structured logging

Both services expose local JSON health/metrics and correlation-aware structured logs. Production exporter, backend, dashboards, alerts, paging, and SLO implementation are deferred rather than implied.

### CORS is a configured allowlist outside Development

`ApiCorsPolicy` allows any origin without credentials in `Development` only. Every other environment allows only the origins in `Cors:AllowedOrigins`, with credentials, and allows none when it is unset. This fails closed: forgetting the setting blocks browser clients rather than exposing the API to every origin. Which origins belong in the production list is still open (item 4 below).

### Tenants can register their own CORS origins, without credentials

Active tenants can also register exact `https` origins (`TenantAllowedOrigins`), managed by tenant administrators and gated by the `feature.security.cors_origins.manage` entitlement. Preflights carry no credentials, so on a shared API host an origin is accepted if any active tenant registered it; on a tenant subdomain only that tenant's origins count. Since one tenant's origin is then accepted for all tenants' routes on a shared host, tenant origins never get `Access-Control-Allow-Credentials`; only `Cors:AllowedOrigins` does. This is safe while the API authenticates with bearer tokens only. Adding cookie-based authentication would need this revisited. The origin lists are cached in Redis and invalidated on change; see [Operations](operations.md#origin-cache).

### The MCP endpoint is an in-process, stateless transport in Presentation

AI clients reach the API through an MCP endpoint at `POST /api/v1/mcp` in the existing host, not a separate service. It is one more presentation transport next to the controllers, so it reuses JWT authentication, `TenantMiddleware`, the plan-aware rate limiter, RBAC policies, entitlements and the Application services instead of re-implementing them. The route follows the `/api/v1/...` convention and is not in any middleware skip list.

- **Stateless Streamable HTTP, set explicitly** even though it is the SDK default. In stateless mode tools resolve services from the HTTP request's own scope, so they see the `ITenantContext` that `TenantMiddleware` set. A stateful session would resolve a fresh, empty tenant context, and the entitlement-gated tools would fail. `McpServerTests` fails if the mode is switched.
- **The tenant always comes from the token.** No tool has a tenant parameter.
- **Same rules as the matching REST action.** Each tool carries the action's RBAC policy (`[Authorize]` with the SDK's authorization filters) and entitlement check, and calls the same Application service; tool classes do not use `ApplicationDbContext`.
- **The endpoint itself requires an authenticated caller** (`RequireAuthorization`, bound to the `McpBearer` scheme), so an anonymous request gets `401` before `TenantMiddleware` could accept a tenant hint for it.
- **Read-only for now.** No create, update, delete, secret-rotation or billing tools, and no secrets in outputs. Write tools would need their own review (confirmation, idempotency, audit).
- **Audience-bound MCP tokens, not MCP OAuth.** The endpoint accepts only tokens issued for it. A client exchanges its API access token at `POST /api/v1/auth/mcp-token` for a token with the same claims (`sub`, `email`, `tenant_id`, `role`, `jti`), `aud` = `Jwt:McpAudience` and a lifetime of `Jwt:McpTokenExpirationMinutes` (15 by default), with no refresh token. Issuance is audited as `MCP_TOKEN_ISSUED`, and the auth-route tenant check refuses a suspended tenant.
  - A second JwtBearer scheme, `McpBearer`, has the same key and issuer but `ValidAudience = Jwt:McpAudience`. The MCP route's policy authenticates with `McpBearer` only, so REST tokens get `401` there. The default scheme keeps `Jwt:Audience`, so MCP tokens get `401` on REST and can't be exchanged for another MCP token.
  - `UseAuthorization` authenticates the policy's scheme and sets `HttpContext.User` before `TenantMiddleware`, so tenant resolution, tool RBAC, the rate limit and auditing work as before.
  - The API refuses to start when `Jwt:McpAudience` is empty or equal to `Jwt:Audience`. Deployments need no new setting: the default `MultiTenantSaasApi/mcp` differs from the default `Jwt:Audience`.
  - This was a breaking change for MCP clients, done without a transition period that accepts both kinds of token, since that would have kept the gap open.
  - Full MCP OAuth (Protected Resource Metadata, an authorization server, scopes) is left until remote clients are in scope; see open item 13.
- **Checked against the MCP authorization spec (revision `2026-07-28`).** Authorization is optional in MCP; where the endpoint uses it, it meets these rules:
  - every request's token is validated (signature, issuer, audience, expiry)
  - invalid or expired tokens get `401` with `WWW-Authenticate: Bearer error="invalid_token"`
  - no token passthrough: tools call Application services in-process, and the only outbound HTTP is the webhook dispatcher, which signs with HMAC and sends no `Authorization`

  The audience rule (tokens issued for this server) is met by the MCP tokens above; until they were added, the audience named the whole API and REST and MCP tokens were interchangeable. One gap is deliberate, acceptable while clients are local and handed a token, and tracked in open item 13: there is no Protected Resource Metadata. RBAC is enforced per tool (the tool is hidden from `tools/list`, and `tools/call` gets a JSON-RPC error), not as the spec's HTTP `403` `insufficient_scope`, which is a SHOULD for scopes the API doesn't have. The confused-deputy risk concerns OAuth proxy servers and doesn't apply.
- **What `McpServerTests` and `McpTokenTests` pin.**
  - A REST (login) token and expired, wrong-key, wrong-audience and wrong-issuer tokens get `401` on MCP with that challenge and no `resource_metadata`; an MCP token gets `401` on REST and on the exchange itself.
  - A tenant hint that disagrees with the token gets `403` `TenantMismatch`.
  - A tenant argument the model adds to a call is ignored by the SDK (observed in 2.4.0), and the tools still use the token's tenant.
  - A deleted user's MCP token still authenticates until it expires (stateless JWT, at most 15 minutes by default), but every tool is hidden and refused, the same way REST refuses policy-gated actions, and the user gets no new MCP token. Actions with only `[Authorize]` would still accept a REST token.
- **Known weakness: a token without `tenant_id` falls back to the tenant hint.** `TenantMiddleware` does this on purpose on every route, so a validly signed token without `tenant_id` plus `X-Tenant-ID` reaches the MCP handler under the hinted tenant. No tool runs: `PermissionAuthorizationHandler` requires the `tenant_id` claim, and every tool has an RBAC policy. A tool without a policy would run, so until open item 14 is decided every MCP tool must carry one. `JwtService` never issues such a token, so exploiting this needs the signing key.

## Open owner-review items

1. Select the environment count and the release/promotion process. The first topology is chosen: one Debian 12 EC2 host provisioned by Terraform and configured by Ansible with native systemd services, growing toward multi-region active-passive DR ([deployment roadmap](deployment-roadmap.md)). Still open: how many environments, and how a release is promoted between them.
2. Decide whether the first deployment is internal-only or public/customer-facing.
3. Select public DNS, TLS termination, and certificate ownership.
4. Decide whether a browser client is in scope and define the production CORS allowlist.
5. Select the runtime secret store and rotation process.
6. Decide whether live billing is required for the first deployment and confirm the first provider.
7. Wire and validate real provider webhook verification, callback transport, and reconciliation data sources when live billing enters scope.
8. Decide whether the file-backed BillingService queue is acceptable for the first environment or replace it with durable shared infrastructure.
9. Select production telemetry export, storage, dashboard, alert, and SLO ownership.
10. Define migration rollback, database backup/restore, disaster recovery, and incident procedures.
11. Decide whether Redis must participate in API readiness checks.
12. Complete and rehearse outbound-webhook active-secret rotation cutover.
13. Decide whether the MCP endpoint must serve remote clients. Its tokens are already audience-bound (see [the MCP decision](#the-mcp-endpoint-is-an-in-process-stateless-transport-in-presentation)). Remote clients would also need the rest of the MCP authorization spec (revision `2026-07-28`):
    - Protected Resource Metadata (RFC 9728), discoverable through `resource_metadata` in the `WWW-Authenticate` challenge or at `/.well-known/oauth-protected-resource`
    - an OAuth 2.1 authorization server (PKCE, resource indicators) whose tokens carry `tenant_id` and the user, replacing the `/auth/mcp-token` exchange
    - scopes mapped to RBAC permissions, with `403` `insufficient_scope` when one is missing
14. Decide whether `TenantMiddleware` should reject an authenticated token without a usable `tenant_id`, instead of falling back to the `X-Tenant-ID` or subdomain hint. Today the fallback is pinned by `TenantMiddlewareTests`; REST actions with only `[Authorize]` and any MCP tool without an RBAC policy would run under the hinted tenant. See [the MCP decision](#the-mcp-endpoint-is-an-in-process-stateless-transport-in-presentation).
