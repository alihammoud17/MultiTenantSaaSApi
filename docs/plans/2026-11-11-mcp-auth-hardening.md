# MCP Auth: Prove and Harden How the Server Authenticates and Resolves the Tenant

- **Date:** Wednesday's focus (week 6, day 3), planned for 11 Nov 2026, worked on 12 Nov 2026
- **Repo:** MultiTenantSaaSApi
- **Branch:** `feat/mcp-auth-hardening` (from `master`)
- **Status:** Blocks 1–3 done; Block 4 (optional) not started
- **Focus:** Secure the server
- **Time:** ~1.5–2 h (Blocks 1–3); Block 4 is optional
- **Companion plan:** ai-labs `docs/plans/2026-11-11-mcp-client.md` (consuming these tools from my `IChatClient` app). Do this plan first.

Tutor mode: I type the commands and write the code. Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over".

## Deliverable
- [x] **D1.** The server authenticates the caller and resolves the tenant from the token, never from model input, and tests prove each part

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

### How the table was filled

The table above is final: reviewed and accepted on 12 Nov 2026. This section records how each cell was established, so the ADR entry in Block 3 can cite it and a later reader can re-check it.

#### Method

1. **Read the repo's own account first:** this plan, `AGENTS.md`, `CLAUDE.md`, the MCP ADR (`docs/decisions.md:49-58`), open item 13 (`docs/decisions.md:74`), `docs/contracts.md#mcp-endpoint` (`docs/contracts.md:124-168`) and `Tests/Integration/McpServerTests.cs`. This gives the claims to verify, not the evidence.
2. **Read the code paths a request takes:** JwtBearer setup and middleware order in `Presentation/Program.cs`, the MCP route and filters in `Presentation/Mcp/McpServerRegistrationExtensions.cs`, `Presentation/Middleware/TenantMiddleware.cs`, `Presentation/Authorization/PermissionAuthorizationHandler.cs`, `Application/Services/RbacAuthorizationService.cs`, the tools in `Presentation/Mcp/Tools/TenantReadTools.cs`, and token issuance in `Application/Services/JwtService.cs`.
3. **Search the code for anything that could forward a token:** across `Presentation`, `Application` and `Infrastructure`, searched for `HttpClient`, `IHttpClientFactory`, `AddHttpClient`, `IHttpContextAccessor`, `DefaultRequestHeaders`, `Headers.Authorization` and `Bearer`, then read every hit.
4. **Read the existing tests that already pin behavior:** `Tests/Integration/McpServerTests.cs` (what MCP proves today) and `Tests/UnitTests/TenantMiddlewareTests.cs` (how the middleware treats claims and hints).
5. **Read the spec pages linked in Resources (fetched 12 Nov 2026), not from memory,** checking each Block 1 bullet against the exact wording and its MUST/SHOULD level. Pages: Authorization (`/specification/latest/`, whose internal links point to revision `2026-07-28`), Authorization server discovery (`2026-07-28`), Security best practices, and the C# SDK v2 Identity and roles page.
6. **Fill each row by comparing:** what the spec requires (step 5) against what the code does (steps 2–4), then mark a gap only where the code does not meet the requirement, and "test missing" where it probably does but nothing proves it.

#### Sources

| Source | What was taken from it |
|---|---|
| [MCP Authorization](https://modelcontextprotocol.io/specification/latest/basic/authorization), *Token Handling*, *Error Handling*, *Access Token Privilege Restriction* | MUST validate tokens; MUST 401 invalid/expired tokens; MUST validate audience (RFC 8707); MUST NOT accept or transit other tokens; MUST implement PRM; SHOULD 403 `insufficient_scope`; applies to HTTP transports only |
| [Authorization server discovery](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization/authorization-server-discovery) | PRM discovery is one of two options: `WWW-Authenticate` `resource_metadata` **or** `/.well-known/oauth-protected-resource`; clients must support both |
| [Security best practices](https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices), *Token Passthrough*, *Confused Deputy Problem* | Passthrough definition and why it's forbidden; confused deputy is about OAuth proxy servers with a static client ID and dynamic client registration |
| [C# SDK v2, Identity and roles](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/identity/identity.html) | With `AddAuthorizationFilters()`, unauthorized items are removed from list results and individual calls get a JSON-RPC "access forbidden" error (no code given); silent on unknown tool arguments |
| [RFC 6750 §3](https://www.rfc-editor.org/rfc/rfc6750#section-3), [RFC 8707](https://www.rfc-editor.org/rfc/rfc8707), [RFC 9728](https://www.rfc-editor.org/rfc/rfc9728) | Background for the `WWW-Authenticate: Bearer error="invalid_token"` format, resource indicators (audience), and PRM |
| `docs/decisions.md:49-58`, `:74`; `docs/contracts.md:124-168` | The decisions already made (bearer tokens, not MCP OAuth; read-only tools; tenant from the token) and the documented failure modes |
| Code and tests listed per row below | The evidence for each "Today" cell |

#### Evidence per row

Line numbers are as of commit `feed20c`.

**1. Tokens validated (MUST, OAuth 2.1 §5.2)**
- Spec: "MCP servers, acting in their role as an OAuth 2.1 resource server, MUST validate access tokens as described in OAuth 2.1 Section 5.2." The only "every request" sentence in the spec is client-side ("authorization MUST be included in every HTTP request from client to server").
- Code: `Presentation/Program.cs:89-103` sets `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime` and `ValidateIssuerSigningKey` to `true`, with `ValidIssuer`, `ValidAudience` and the signing key from `Jwt:*`. `Presentation/Program.cs:221-225` runs `UseAuthentication` → `UseAuthorization` → `TenantMiddleware`. `Presentation/Mcp/McpServerRegistrationExtensions.cs:15` sets `HttpServerSessionMode.Stateless`, so each POST is authenticated on its own; there is no session that could carry an earlier authentication forward.
- Verdict: no gap. All four token properties are checked on each request.

**2. Invalid/expired token → 401 (MUST)**
- Spec: "Invalid or expired tokens MUST receive a HTTP 401 response." The Error Handling table lists 401 as "Authorization required or token invalid".
- Code: `Presentation/Mcp/McpServerRegistrationExtensions.cs:24-28` maps the route with `RequireAuthorization()`; a request whose token fails JwtBearer validation is unauthenticated, so `UseAuthorization` challenges with 401 before `TenantMiddleware` or the MCP handler run. JwtBearer's challenge is expected to carry `WWW-Authenticate: Bearer error="invalid_token"` (RFC 6750 format).
- Tests: `Tests/Integration/McpServerTests.cs:29` (`NoToken_ShouldReturn401`) and `:41` (`NoToken_WithTenantHint_ShouldReturn401`) cover only a missing token. `docs/contracts.md:153` documents "Missing or invalid token → 401", but nothing tests an invalid one.
- Verdict: behavior probably compliant, unproven. The header contents are expected, not observed; steps 2.1–2.3 pin them.

**3. Audience = this server (MUST, RFC 8707)**
- Spec: "MCP servers MUST validate that access tokens were issued specifically for them as the intended audience, according to RFC 8707 Section 2."
- Code: `Application/Services/JwtService.cs:44` writes `aud` = `Jwt:Audience` on every token; `Presentation/appsettings.json:8-13` defines one audience (`MultiTenantSaasApi`) for the whole API; `Presentation/Program.cs:99` accepts exactly that audience. So the audience *is* validated, but it names the whole API, not the MCP resource: a REST token works on `/api/v1/mcp` and an MCP caller's token works on REST.
- Verdict: gap. Acceptable while MCP and REST are one resource used by trusted local clients (`docs/decisions.md:58`); must change for remote clients (Block 4, open item 13).

**4. No token passthrough (MUST NOT accept or transit other tokens)**
- Spec: "MCP servers MUST only accept tokens that are valid for use with their own resources" and "MUST NOT accept or transit any other tokens." Best practices: passthrough is "explicitly forbidden in the authorization specification".
- Code: the search in method step 3 found one registered client, `Presentation/Program.cs:83` (`AddHttpClient("outbound-webhooks")`), used only in `Presentation/Services/OutboundWebhookDeliveryDispatcher.cs:62-89`. That is a background service with no request context: it signs each delivery with the endpoint's HMAC secret (`:87`, `X-Tenant-Webhook-Signature`) and sets no `Authorization` header. `IHttpContextAccessor` appears only in `Application/Services/AuditService.cs` and `Application/Services/EntitlementEvaluator.cs`, which read request data and make no HTTP calls. `Presentation/Mcp/Tools/TenantReadTools.cs` calls Application services in-process. The only other `Authorization` / `Bearer` hits are the Swagger security definition in `Program.cs`.
- Verdict: no gap (this is also step 2.8's evidence). "Accepts only its own tokens" holds only with "own" meaning the whole API; see row 3.

**5. Protected Resource Metadata (MUST)**
- Spec: "MCP servers MUST implement OAuth 2.0 Protected Resource Metadata (RFC 9728)." Discovery page: servers "MUST implement one of the following discovery mechanisms": the `resource_metadata` parameter in `WWW-Authenticate` on 401, or a well-known URI (`/.well-known/oauth-protected-resource`, at the root or path-based).
- Code: no PRM document, no well-known route, no `resource_metadata` in any challenge, and no OAuth authorization server. Clients get a token from `POST /api/v1/auth/login`. `docs/decisions.md:58` ("Bearer tokens, not MCP OAuth"), `docs/decisions.md:74` (open item 13) and `docs/contracts.md:134` ("There is no MCP OAuth discovery") record this as deliberate.
- Verdict: gap, deliberately deferred. PRM exists so a client can discover how to obtain a token by itself; our clients are handed one. It becomes necessary with remote clients (open item 13).

**6. 403 + `insufficient_scope` (SHOULD)**
- Spec: for insufficient scope "the server SHOULD respond with HTTP 403 Forbidden" and `error="insufficient_scope"`. The Error Handling table lists 403 as "Invalid scopes or insufficient permissions".
- Code: no scopes exist in our tokens (`Application/Services/JwtService.cs` issues `sub`, `email`, `tenant_id`, `role`, `jti`). Permissions are RBAC, enforced per tool: `Presentation/Mcp/McpServerRegistrationExtensions.cs:16` (`AddAuthorizationFilters()`), the `[Authorize(Policy = …)]` attributes at `Presentation/Mcp/Tools/TenantReadTools.cs:26, 44, 66, 81`, and `Presentation/Authorization/PermissionAuthorizationHandler.cs:22-27` (needs the `sub` and `tenant_id` claims, then checks the database through `RbacAuthorizationService`). `Tests/Integration/McpServerTests.cs:122-140` pins the result: the tool is left out of `tools/list`, and calling it returns a JSON-RPC error ("Access forbidden", `-32600`) inside HTTP 200. That matches the SDK page (filtered lists, JSON-RPC error on calls). Whole-request rejections are HTTP 403: `TenantSuspended` and `TenantMismatch` in `Presentation/Middleware/TenantMiddleware.cs`.
- Verdict: no violation. There are no scopes, the rule is a SHOULD, and per-tool RBAC is a different layer from the spec's whole-request scope check. Revisit when scopes are mapped to RBAC (open item 13).

**7. Tenant never from model input (our own rule, not a spec rule)**
- Code: no tool parameter carries a tenant; the tools read `tenantContext.TenantId` (`Presentation/Mcp/Tools/TenantReadTools.cs:71, 84, 91`). `Presentation/Middleware/TenantMiddleware.cs` takes a hint from the subdomain or `X-Tenant-ID` (`:81`), rejects a hint that disagrees with the claim with 403 `TenantMismatch` (`:96`), and otherwise uses the claim, falling back to the hint (`:102`); `GetJwtTenantId` (`:131`) reads `tenant_id` only from an authenticated identity.
- Tests: `Tests/Integration/McpServerTests.cs:56` proves no tool's input schema contains "tenant". `Tests/UnitTests/TenantMiddlewareTests.cs:213` pins that an authenticated token without a usable `tenant_id` falls back to the hint, which is why 2.4 exists.
- Verdict: holds in code; tests missing for a smuggled `tenantId` argument (2.6), token for A with a hint for B (2.5), and a tenant-less token with a hint (2.4).

**8. Confused deputy**
- Best practices: the attack targets MCP proxy servers that use a static client ID with a third-party authorization server while allowing dynamic client registration; a consent cookie lets an attacker skip consent and obtain an authorization code.
- Code: none of those parts exist. No OAuth proxy, no third-party authorization server, no dynamic client registration (`docs/decisions.md:58`).
- Verdict: not applicable. Re-check if an OAuth authorization server is added (open item 13).

#### Limits of this research

- The Authorization page was read at `/specification/latest/`. The page shows no version; revision `2026-07-28` is inferred from its internal links.
- The `WWW-Authenticate` header in row 2 is expected from JwtBearer's defaults, not observed. Step 2.1 records what is actually sent.
- The SDK docs don't say how unknown tool arguments are handled; step 2.6 records it.
- The passthrough search covers the .NET API only. BillingService doesn't receive caller tokens and its default callback publisher is a no-op (`CLAUDE.md`, "What is not live").

---

## Block 2: Tests that prove D1 (60 min)

Add to `Tests/Integration/McpServerTests.cs` (raw HTTP where the claim is "rejected before MCP runs", the SDK client where the claim is about tool behavior).

Minting the bad tokens: the factory's JWT settings are inline literals today (`ApiWebApplicationFactory.cs`). First expose them as constants on the factory (`JwtSecret`, `JwtIssuer`, `JwtAudience`, same pattern as `TenantOperationsSecret`) and add one helper that mints a token with `JwtSecurityTokenHandler` from subject, tenant id (nullable), expiry, signing key, issuer and audience. For a real subject, read `sub` from a login token with `JwtSecurityTokenHandler.ReadJwtToken`.

- [x] **2.0 Token-minting helper** and factory JWT constants (above)
  - **Done:** `Tests/Integration/ApiWebApplicationFactory.cs` exposes `JwtSecret`, `JwtIssuer` and `JwtAudience` and uses them in its settings (values unchanged). `Tests/Integration/SecurityTestHelpers.cs` adds `CreateTestJwt(userId, tenantId?, expiresUtc, role, signingKey, issuer, audience)`, which builds the same claims and HS256 signature as `JwtService`, leaves out `tenant_id` when `tenantId` is null, and sets `nbf` before a past expiry (`JwtSecurityToken` rejects `nbf` ≥ `exp`). `GetUserId(token)` reads `sub` from a login token so minted tokens name a real user.
  - **Positive control:** `MintedValidToken_ShouldBeAccepted` mints a valid token for a real admin and expects `tools/list` → 200. Without it, the 401 tests below would also pass if the helper produced tokens the server could never accept.
- [x] **2.1 Expired token** → 401, and the response has a `WWW-Authenticate: Bearer` header with `error="invalid_token"`. JwtBearer's default `ClockSkew` is 5 minutes, so set `exp` at least 10 minutes in the past (with `nbf` before it). Pin the scheme and `error`, not `error_description` (a JwtBearer detail).
  - **Done:** `InvalidToken_ShouldReturn401WithBearerInvalidTokenChallenge("expired")`, raw HTTP `tools/list`. The token is identical to the control except `exp` = now − 10 min. Asserts 401, exactly one `WWW-Authenticate` challenge, scheme `Bearer`, parameter containing `error="invalid_token"`, and **no** `resource_metadata` (no PRM yet, open item 13; adding it must change this test on purpose).
  - **Observed** (by temporarily failing the assertion, then restoring it): `Bearer error="invalid_token", error_description="The token expired at '…'"`. The description is not pinned.
- [x] **2.2 Wrong signing key** → 401
  - **Done:** same theory, case `"wrong-signing-key"`: valid claims, signed with a different key of 32+ bytes (so HS256 signing itself doesn't throw and only validation can fail). Same assertions as 2.1.
  - **Observed:** `error="invalid_token", error_description="The signature key was not found"`.
- [x] **2.3 Wrong audience** and **wrong issuer** → 401
  - **Done:** same theory, cases `"wrong-audience"` (`aud` = `https://other-resource.example.com`) and `"wrong-issuer"` (`iss` = `https://other-issuer.example.com`). Same assertions as 2.1. One theory for 2.1–2.3 keeps the four cases identical except for the one property under test.
  - **Observed:** `error_description="The audience '(null)' is invalid"` and `"The issuer 'https://other-issuer.example.com' is invalid"`. The `(null)` is JwtBearer's message reading an exception property the token library leaves unset, not a missing `aud` in the token. Each of the four cases failed for its own reason, so none passes because of an unrelated defect.
- [x] **2.4 Token without a `tenant_id` claim** (validly signed, real user as `sub`). `TenantMiddleware` deliberately falls back to the hint when an authenticated token has no valid `tenant_id` (pinned by `TenantMiddlewareTests.JwtWithMalformedTenantClaim_ShouldFallBackToTheHeaderHint`), so with a hint the request reaches the MCP handler. What should stop it is `PermissionAuthorizationHandler`, which requires a parsable `tenant_id` claim, and every tool has a policy. Pin:
  - no hint → 400 `TenantNotFound`
  - with `X-Tenant-ID` → `tools/list` succeeds with no tools
  - with `X-Tenant-ID` → `tools/call` is refused ("Access forbidden")
  If a tool actually runs, that's a finding: stop and decide the fix together. Otherwise, record "reject authenticated tokens without `tenant_id`" as a follow-up (it changes the pinned middleware behavior and affects REST `[Authorize]`-only endpoints too), not a fix in this slice.
  - **Done, no hint:** `TokenWithoutTenantClaim_WithoutHint_ShouldReturn400TenantNotFound`, raw HTTP: a tenant-less token for a real admin → 400, body contains `TenantNotFound`.
  - **Done, with hint:** `TokenWithoutTenantClaim_WithTenantHint_ShouldReachNoTool`, SDK client (the claim is about tools). Tenant A's admin, token without `tenant_id`, `X-Tenant-ID` = tenant B, which owns a webhook endpoint so a leak would be visible. The MCP handshake succeeds (the request does reach the MCP handler under tenant B), `tools/list` is empty, and calling `list_webhook_endpoints` and `list_audit_logs` both fail with JSON-RPC `InvalidRequest` "Access forbidden".
  - **Result: not a finding.** No tool runs. The only barrier is that every tool has an RBAC policy, so a future tool with plain `[Authorize]` or no policy would run under the hinted tenant. Follow-up for the ADR (Block 3): reject authenticated tokens without `tenant_id`.
- [x] **2.5 Token for tenant A + `X-Tenant-ID` of tenant B** → 403 `TenantMismatch`
  - **Done:** `TokenForTenantA_WithTenantBHint_ShouldReturn403TenantMismatch`, raw HTTP with a real login token for A and B's id as the hint → 403, body contains `TenantMismatch`. `contracts.md` already documented this for MCP; now a test proves it.
- [x] **2.6 Smuggled tenant argument:** tenant A calls `list_audit_logs` (has real parameters) and `list_webhook_endpoints` (no parameters) with an extra `tenantId` (and `tenant_id`) argument set to tenant B's id → only tenant A's data comes back. The SDK docs don't say how unknown arguments are handled, so pin whether the SDK ignores them or rejects the call; both are safe, but the test documents which.
  - **Done:** `SmuggledTenantArgument_ShouldBeIgnored_AndReturnOnlyTheCallersData`, SDK client. Both tenants get a webhook endpoint through REST. As A: `list_webhook_endpoints` with `tenantId` and `tenant_id` = B returns exactly `a-hook`; `list_audit_logs` with `pageSize` = 200 plus the same two arguments returns a non-empty list in which every entry is tenant A's.
  - **Pinned:** SDK 2.4.0 **ignores** unknown arguments (the call succeeds, no error), and the tools keep reading the tenant from the token.
- [x] **2.7 Deleted user:** there is no user deactivation, only hard delete (`AdminTenantManagementController.RemoveUser`, which cascades `UserRoles`). REST has no per-request user check in middleware, but every RBAC policy queries the user through `RbacAuthorizationService`, so a deleted user's still-valid token fails policy-gated endpoints. Delete the user through `ApplicationDbContext` (the REST delete needs step-up and an entitlement, which aren't under test), then pin: the token still authenticates; REST `GET /api/v1/tenant/webhook-endpoints` → 403; MCP `tools/list` hides the tools and `tools/call` is refused. Write down the known limit: `[Authorize]`-only endpoints and the JWT itself stay valid until expiry.
  - **Done:** `DeletedUser_ShouldBeRefusedOnMcp_LikeOnRest`. Before the deletion (control): REST `GET /api/v1/tenant/webhook-endpoints` → 200 and MCP `tools/list` includes `list_webhook_endpoints`, so the refusals afterwards come from the deletion. The admin is removed with `ApplicationDbContext` (every dependent relationship cascades; `AuditLog.UserId` has no foreign key). After: REST → 403; the MCP handshake still succeeds (the token still authenticates), `tools/list` is empty, and `list_webhook_endpoints` is refused with "Access forbidden". MCP and REST behave the same.
  - **Known limit** (for the ADR): the JWT stays valid until it expires; only policy-gated actions refuse a deleted user.
- [x] **2.8 No passthrough:** confirm by reading the code that no tool or filter makes an outbound HTTP call with the caller's token (one line in the ADR is enough; no test needed). Setup check: the only outbound HTTP is `OutboundWebhookDeliveryDispatcher`, which signs with the endpoint's HMAC secret and never sets `Authorization`.
  - **Done by reading the code**, see Block 1, "Evidence per row", row 4: one registered `HttpClient` (`outbound-webhooks`), used only by the background webhook dispatcher, which sets HMAC `X-Tenant-Webhook-*` headers and no `Authorization`; the tools and the audit filter make no HTTP calls.
- [x] All tests pass: `dotnet test --no-build`
  - **Run on 12 Nov 2026:** `dotnet build --no-restore` → 0 errors, 613 warnings (same count as before this block, none in the changed files). `dotnet test --no-build --filter "FullyQualifiedName~Tests.Integration.McpServerTests"` → 18 passed (8 existing + 10 new). `dotnet test --no-build` → 492 passed, 0 failed, 40 skipped. The skipped tests are the Testcontainers PostgreSQL/Redis tests; Docker was not reachable.
  - Shared test helpers added in `McpServerTests.cs`: `SendRawToolsListAsync` (token plus optional hint), `GetAsAsync` (REST status), `AssertToolCallForbiddenAsync` (the RBAC denial shape already pinned in `Member_ShouldNotSeeOrCallAuditTools`), and an optional `tenantHint` on `CreateMcpClientAsync`.

---

## Block 3: Docs (20 min)

- [x] `docs/decisions.md`: extend the MCP ADR with the spec comparison (Block 1 table, short, citing revision `2026-07-28`), what the new tests pin, and the tenant-less-token follow-up from 2.4
- [x] Update open item 13 with the concrete list for remote clients: audience-bound tokens for the MCP resource, Protected Resource Metadata discoverable via `WWW-Authenticate` `resource_metadata` or `/.well-known/oauth-protected-resource`, an OAuth 2.1 authorization server (PKCE, resource indicators), scopes mapped to RBAC permissions
- [x] `docs/contracts.md#mcp-endpoint`: the 401 cases and the `WWW-Authenticate` header, plus Failure modes rows for a tenant-less token and a deleted user
- [x] `docs/CHANGELOG.md` entry
- [x] Run and report the AGENTS.md .NET validation commands; commit, push, PR

---

## Block 4: Stretch (optional, 60–90 min): audience-bound MCP tokens

Only if Blocks 1–3 are done and there's time left. This is the spec's "issued for this server" rule, done without a full OAuth server.
- [ ] Design first (write it in the ADR before coding): a short-lived token with `aud` = the MCP resource (e.g. `https://<host>/api/v1/mcp`), issued by an authenticated call (e.g. `POST /api/v1/auth/mcp-token`), accepted **only** on `/api/v1/mcp`; REST rejects it and MCP rejects REST tokens
- [ ] Decide together whether to build it now or leave it in open item 13. The ai-labs client plan works either way, because its token provider just calls whichever endpoint issues the token.

---

## End of block
- [x] D1 checked
- [ ] Continue with the ai-labs plan (`docs/plans/2026-11-11-mcp-client.md`)
