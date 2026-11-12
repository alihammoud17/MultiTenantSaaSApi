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

- [x] Read the Authorization page (Token handling, Error handling) and the token-passthrough and confused-deputy parts of the security best practices
- [x] Fill in this table in your own words (it goes into `docs/decisions.md` in Block 3):

| Spec rule | Today | Gap? |
|---|---|---|
| Tokens validated (MUST, OAuth 2.1 §5.2) | JwtBearer checks signature, issuer, audience and expiry on every request. The transport is stateless, so no session skips the check. | No |
| Invalid/expired token → 401 (MUST) | `RequireAuthorization()` rejects before `TenantMiddleware` and the MCP handler. JwtBearer should send `WWW-Authenticate: Bearer error="invalid_token"`. | Behavior: probably no gap. Proof: **tests missing** for expired, wrong key, wrong audience and wrong issuer (2.1–2.3). The header is expected but not yet confirmed. |
| Audience = this server (MUST, RFC 8707) | One `Jwt:Audience` for the whole API, so a REST token works on MCP and the other way round. | **Yes.** Acceptable while MCP and REST are one resource with trusted, local clients. Must change for remote clients (Block 4, open item 13). |
| No token passthrough (MUST NOT accept or transit other tokens) | Tools call Application services in-process. The only outbound HTTP is the webhook dispatcher, which signs with HMAC and sends no `Authorization`. | No. Checked by reading the code (2.8). Our server also accepts only its own tokens, but "its own" covers REST and MCP together; see the audience row. |
| Protected Resource Metadata (MUST), discovered via `WWW-Authenticate` `resource_metadata` **or** `/.well-known/oauth-protected-resource` | Not implemented. No OAuth authorization server; clients are given a token from `POST /api/v1/auth/login`. | **Yes**, deliberately deferred. It only matters once clients must discover how to authorize on their own (remote clients): open item 13. |
| 403 + `insufficient_scope` (SHOULD) | No scopes exist. Missing RBAC permissions are handled per tool: the tool is hidden from `tools/list`, and `tools/call` returns a JSON-RPC error inside HTTP 200. Whole-request rejections (suspended tenant, tenant mismatch) are HTTP 403. | No violation today (no scopes, and it's a SHOULD). Different layer: the spec's 403 is for whole-request scope failures. Revisit when scopes map to RBAC (open item 13). |
| Tenant never from model input (our rule) | No tool has a tenant parameter (the schema test proves it). The tenant comes from the `tenant_id` claim through `ITenantContext`, and a conflicting hint gets 403 `TenantMismatch`. | **Tests missing:** smuggled `tenantId` argument (2.6), token for A + hint for B (2.5), tenant-less token + hint (2.4). |
| Confused deputy | No OAuth proxy, no third-party authorization server, no dynamic client registration. | N/A |

### References for the table

Start with `Presentation/Program.cs:89-103`, `Presentation/Middleware/TenantMiddleware.cs:81-102` and the spec's *Token Handling* section; they cover most rows.

| Row | Code and docs | Spec and standards |
|---|---|---|
| Tokens validated | `Presentation/Program.cs:89-103` (JwtBearer: issuer, audience, lifetime, signing key all validated); `Presentation/Program.cs:221-225` (`UseAuthentication` → `UseAuthorization` → `TenantMiddleware`); `Presentation/Mcp/McpServerRegistrationExtensions.cs:15` (stateless: each POST carries its own token) | Authorization, *Token Handling* |
| Invalid/expired → 401 | `Presentation/Mcp/McpServerRegistrationExtensions.cs:24-28` (`RequireAuthorization()`); `Tests/Integration/McpServerTests.cs:29` and `:41` (only "no token" is tested today); `docs/contracts.md:153` (Failure modes) | Authorization, *Error Handling*; [RFC 6750 §3](https://www.rfc-editor.org/rfc/rfc6750#section-3) (`WWW-Authenticate: Bearer error="invalid_token"`) |
| Audience = this server | `Application/Services/JwtService.cs:44` (`aud` = `Jwt:Audience`); `Presentation/appsettings.json:8-13` (one audience for the whole API); `Presentation/Program.cs:99` (`ValidAudience`) | Authorization, *Token Handling*; [RFC 8707](https://www.rfc-editor.org/rfc/rfc8707) |
| No token passthrough | `Presentation/Program.cs:83` (the only `HttpClient`); `Presentation/Services/OutboundWebhookDeliveryDispatcher.cs:62-89` (background job, HMAC signature at `:87`, no `Authorization`); `Presentation/Mcp/Tools/TenantReadTools.cs` (in-process service calls) | Authorization, *Access Token Privilege Restriction*; Security best practices, *Token Passthrough* |
| Protected Resource Metadata | `docs/decisions.md:58` ("Bearer tokens, not MCP OAuth"); `docs/decisions.md:74` (open item 13); `docs/contracts.md:134` (no MCP OAuth discovery) | [Authorization server discovery](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization/authorization-server-discovery); [RFC 9728](https://www.rfc-editor.org/rfc/rfc9728) |
| 403 for insufficient scope | `Presentation/Mcp/McpServerRegistrationExtensions.cs:16` (`AddAuthorizationFilters()`); `Presentation/Mcp/Tools/TenantReadTools.cs:26, 44, 66, 81` (tool policies); `Tests/Integration/McpServerTests.cs:122-140` (JSON-RPC "Access forbidden" inside HTTP 200); `Presentation/Authorization/PermissionAuthorizationHandler.cs:22-27` (`tenant_id` + `sub` claims, then a DB lookup) | Authorization, *Error Handling*; [C# SDK v2, Identity and roles](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/identity/identity.html) |
| Tenant never from model input | `Presentation/Mcp/Tools/TenantReadTools.cs:71, 84, 91` (`tenantContext.TenantId`); `Presentation/Middleware/TenantMiddleware.cs:81` (header hint), `:96` (`TenantMismatch`), `:102` (claim, else hint), `:131` (`GetJwtTenantId`); `Tests/Integration/McpServerTests.cs:56` (no "tenant" input in any schema); `Tests/UnitTests/TenantMiddlewareTests.cs:213` (fallback to the hint) | Not a spec rule; our own invariant |
| Confused deputy | `docs/decisions.md:58` (no OAuth proxy, no third-party authorization server) | Security best practices, *Confused Deputy Problem* |

Background: `docs/decisions.md:49-58` (the MCP ADR), `docs/contracts.md:124-168` (the MCP endpoint contract), `Tests/Integration/ApiWebApplicationFactory.cs:42-45` (test JWT settings, needed in 2.0).

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
