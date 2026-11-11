# MCP Auth: Prove and Harden How the Server Authenticates and Resolves the Tenant

- **Date:** Wednesday's focus (week 6, day 3), planned for 11 Nov 2026, worked on 12 Nov 2026
- **Repo:** MultiTenantSaaSApi
- **Branch:** `feat/mcp-auth-hardening` (from `master`)
- **Status:** in progress
- **Focus:** Secure the server
- **Time:** ~1.5–2 h (Blocks 1–3); Block 4 is optional
- **Companion plan:** ai-labs `docs/plans/2026-11-11-mcp-client.md` (consuming these tools from my `IChatClient` app). Do this plan first.

Tutor mode: I type the commands and write the code. Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over".

## Deliverable
- [ ] **D1.** The server authenticates the caller and resolves the tenant from the token, never from model input, and tests prove each part

## Where we start
Most of D1 already exists from PR #156: `RequireAuthorization()` on `/api/v1/mcp`, JWT bearer validation on every request (the transport is stateless), `TenantMiddleware` takes the tenant from the `tenant_id` claim, no tool has a tenant parameter, and `McpServerTests` covers no token, a tenant hint without a token, isolation, RBAC, entitlements and suspension.

Today's job is to check that against the MCP authorization spec, close the test gaps, and write down what's still missing for remote clients.

## Resources
- MCP spec, Authorization (`latest` links to revision `2026-07-28`; cite that revision): https://modelcontextprotocol.io/specification/latest/basic/authorization
- MCP spec, Authorization server discovery (PRM discovery options): https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization/authorization-server-discovery
- MCP spec, Security best practices (token passthrough, confused deputy): https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices
- modelcontextprotocol.io, What is MCP? (architecture): https://modelcontextprotocol.io/docs/getting-started/intro
- MCP C# SDK v2, Identity and roles: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/identity/identity.html
- MCP C# SDK, ProtectedMcpServer sample: https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/ProtectedMcpServer
- Repo: `docs/decisions.md` (the MCP ADR and open item 13), `docs/contracts.md#mcp-endpoint`, `Tests/Integration/McpServerTests.cs`

---

## Block 1: Read the spec and compare (30 min)

Authorization is **optional** in MCP and applies to HTTP transports only. When an HTTP server supports it, the spec's server-side rules include:
- **MUST** validate access tokens (OAuth 2.1 §5.2); invalid or expired tokens **MUST** get **401**. ("Every request" is the client-side rule: the client sends the token on every request. A stateless server validates each one.)
- **MUST** validate that the token was issued **for this server** as the intended audience (RFC 8707)
- **MUST** only accept tokens valid for its own resources and **MUST NOT** accept or transit any other tokens (no token passthrough)
- **MUST** implement **Protected Resource Metadata** (RFC 9728), discoverable through **either** a `resource_metadata` parameter in `WWW-Authenticate` on 401 **or** the well-known URI `/.well-known/oauth-protected-resource`
- **SHOULD** return **403** with `error="insufficient_scope"` for insufficient scopes
- Confused deputy (best practices) concerns MCP proxy servers that use a static client ID with a third-party authorization server and dynamic client registration: not applicable here (no OAuth proxy), say so in the ADR

- [ ] Read the Authorization page (Token handling, Error handling) and the token-passthrough and confused-deputy parts of the security best practices
- [ ] Fill in this table in your own words (it goes into `docs/decisions.md` in Block 3):

| Spec rule | Today | Gap? |
|---|---|---|
| Tokens validated (MUST, OAuth 2.1 §5.2) | JwtBearer validates signature, issuer, audience, lifetime on each request (stateless) | |
| Invalid/expired token → 401 | | test missing for expired / wrong key / wrong audience / wrong issuer |
| Audience = this server | Same `Jwt:Audience` as the REST API: a REST token works on MCP and vice versa | **yes**: acceptable while MCP is one resource with the API; must change for remote clients (Block 4) |
| No token passthrough | Tools call Application services in-process; the token goes nowhere | check: no outbound call forwards `Authorization` |
| Protected Resource Metadata, discoverable via `WWW-Authenticate: Bearer resource_metadata=…` **or** `/.well-known/oauth-protected-resource` | Not implemented (no OAuth authorization server) | **yes**: open item 13 |
| 403 for insufficient scope (SHOULD) | RBAC denial is a JSON-RPC error inside a 200 (tool-level, from `AddAuthorizationFilters`) | note the difference: HTTP 403 is for whole-request scope failures |
| Tenant never from model input | No tool parameter; `ITenantContext` from the claim | test missing for a smuggled argument |
| Confused deputy | No OAuth proxy, no third-party authorization server | N/A |

---

## Block 2: Tests that prove D1 (60 min)

Add to `Tests/Integration/McpServerTests.cs` (raw HTTP where the claim is "rejected before MCP runs", the SDK client where the claim is about tool behavior).

Minting the bad tokens: the factory's JWT settings are inline literals today (`ApiWebApplicationFactory.cs`). First expose them as constants on the factory (`JwtSecret`, `JwtIssuer`, `JwtAudience`, same pattern as `TenantOperationsSecret`) and add one helper that mints a token with `JwtSecurityTokenHandler` from subject, tenant id (nullable), expiry, signing key, issuer and audience. For a real subject, read `sub` from a login token with `JwtSecurityTokenHandler.ReadJwtToken`.

- [ ] **2.0 Token-minting helper** and factory JWT constants (above)
- [ ] **2.1 Expired token** → 401, and the response has a `WWW-Authenticate: Bearer` header with `error="invalid_token"`. JwtBearer's default `ClockSkew` is 5 minutes, so set `exp` at least 10 minutes in the past (with `nbf` before it). Pin the scheme and `error`, not `error_description` (a JwtBearer detail).
- [ ] **2.2 Wrong signing key** → 401
- [ ] **2.3 Wrong audience** and **wrong issuer** → 401
- [ ] **2.4 Token without a `tenant_id` claim** (validly signed, real user as `sub`). `TenantMiddleware` deliberately falls back to the hint when an authenticated token has no valid `tenant_id` (pinned by `TenantMiddlewareTests.JwtWithMalformedTenantClaim_ShouldFallBackToTheHeaderHint`), so with a hint the request reaches the MCP handler. What should stop it is `PermissionAuthorizationHandler`, which requires a parsable `tenant_id` claim, and every tool has a policy. Pin:
  - no hint → 400 `TenantNotFound`
  - with `X-Tenant-ID` → `tools/list` succeeds with no tools
  - with `X-Tenant-ID` → `tools/call` is refused ("Access forbidden")
  If a tool actually runs, that's a finding: stop and decide the fix together. Otherwise, record "reject authenticated tokens without `tenant_id`" as a follow-up (it changes the pinned middleware behavior and affects REST `[Authorize]`-only endpoints too), not a fix in this slice.
- [ ] **2.5 Token for tenant A + `X-Tenant-ID` of tenant B** → 403 `TenantMismatch`
- [ ] **2.6 Smuggled tenant argument:** tenant A calls `list_audit_logs` (has real parameters) and `list_webhook_endpoints` (no parameters) with an extra `tenantId` (and `tenant_id`) argument set to tenant B's id → only tenant A's data comes back. The SDK docs don't say how unknown arguments are handled, so pin whether the SDK ignores them or rejects the call; both are safe, but the test documents which.
- [ ] **2.7 Deleted user:** there is no user deactivation, only hard delete (`AdminTenantManagementController.RemoveUser`, which cascades `UserRoles`). REST has no per-request user check in middleware, but every RBAC policy queries the user through `RbacAuthorizationService`, so a deleted user's still-valid token fails policy-gated endpoints. Delete the user through `ApplicationDbContext` (the REST delete needs step-up and an entitlement, which aren't under test), then pin: the token still authenticates; REST `GET /api/v1/tenant/webhook-endpoints` → 403; MCP `tools/list` hides the tools and `tools/call` is refused. Write down the known limit: `[Authorize]`-only endpoints and the JWT itself stay valid until expiry.
- [ ] **2.8 No passthrough:** confirm by reading the code that no tool or filter makes an outbound HTTP call with the caller's token (one line in the ADR is enough; no test needed). Setup check: the only outbound HTTP is `OutboundWebhookDeliveryDispatcher`, which signs with the endpoint's HMAC secret and never sets `Authorization`.
- [ ] All tests pass: `dotnet test --no-build`

---

## Block 3: Docs (20 min)

- [ ] `docs/decisions.md`: extend the MCP ADR with the spec comparison (Block 1 table, short, citing revision `2026-07-28`), what the new tests pin, and the tenant-less-token follow-up from 2.4
- [ ] Update open item 13 with the concrete list for remote clients: audience-bound tokens for the MCP resource, Protected Resource Metadata discoverable via `WWW-Authenticate` `resource_metadata` or `/.well-known/oauth-protected-resource`, an OAuth 2.1 authorization server (PKCE, resource indicators), scopes mapped to RBAC permissions
- [ ] `docs/contracts.md#mcp-endpoint`: the 401 cases and the `WWW-Authenticate` header, plus Failure modes rows for a tenant-less token and a deleted user
- [ ] `docs/CHANGELOG.md` entry
- [ ] Run and report the AGENTS.md .NET validation commands; commit, push, PR

---

## Block 4: Stretch (optional, 60–90 min): audience-bound MCP tokens

Only if Blocks 1–3 are done and there's time left. This is the spec's "issued for this server" rule, done without a full OAuth server.
- [ ] Design first (write it in the ADR before coding): a short-lived token with `aud` = the MCP resource (e.g. `https://<host>/api/v1/mcp`), issued by an authenticated call (e.g. `POST /api/v1/auth/mcp-token`), accepted **only** on `/api/v1/mcp`; REST rejects it and MCP rejects REST tokens
- [ ] Decide together whether to build it now or leave it in open item 13. The ai-labs client plan works either way, because its token provider just calls whichever endpoint issues the token.

---

## End of block
- [ ] D1 checked
- [ ] Continue with the ai-labs plan (`docs/plans/2026-11-11-mcp-client.md`)
