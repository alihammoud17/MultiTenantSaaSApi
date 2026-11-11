# MCP Server for the SaaS API: Tenant-Scoped Read Tools

- **Date:** Wed 11 Nov 2026 (week 6, Tuesday's plan, done on day 3)
- **Repo:** MultiTenantSaaSApi
- **Branch:** `feat/mcp-tenant-read-tools` (from `master`)
- **Status:** planned
- **Focus:** Expose real capabilities over MCP
- **Time:** ~4–5 h (Blocks 1–4 are the minimum; Block 5 is optional)

Tutor mode: I type the commands and write the code. Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over".

## Deliverables
- [ ] **D1.** ASP.NET Core MCP server (`ModelContextProtocol.AspNetCore`) with 4 tenant-scoped read tools
- [ ] **D2.** Working from Claude Code

## Resources
- MCP C# SDK docs (v2): https://csharp.sdk.modelcontextprotocol.io/v2/
  - Transports: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/transports/transports.html
  - Stateless and stateful: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html
  - Tools: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/tools/tools.html
  - HTTP context: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/httpcontext/httpcontext.html
  - Identity and roles: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/identity/identity.html
  - Filters: https://csharp.sdk.modelcontextprotocol.io/v2/concepts/filters.html
  - ProtectedMcpServer sample: https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/ProtectedMcpServer
- Microsoft Learn quickstart (yesterday's base): https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server
- Claude Code MCP docs: https://code.claude.com/docs/en/mcp
- Repo rules: `AGENTS.md`, `CLAUDE.md`, `docs/architecture.md`, `docs/contracts.md`, `docs/decisions.md`

---

## Design decisions (read before coding)

1. **In-process, in `Presentation`.** The MCP endpoint is one more transport in the existing host, like the controllers. It's not a new project. That way it reuses everything that already protects the API: JWT bearer auth, `TenantMiddleware`, `RateLimitMiddleware` (plan-aware), RBAC policies, entitlements and the Application services.
2. **Endpoint: `POST /api/v1/mcp`** (Streamable HTTP), so it follows the repo's `/api/v1/...` route convention. It isn't in the skip lists, so `TenantMiddleware` resolves the tenant from the JWT `tenant_id` claim and `RateLimitMiddleware` counts each MCP request.
3. **Stateless HTTP, set explicitly.** In v2 stateless is the default, but set it anyway: in stateless mode, tools get services from `HttpContext.RequestServices`, **the same DI scope** as the middleware. That's what makes the `ITenantContext` set by `TenantMiddleware` visible inside the tools. In stateful mode each handler gets a new scope, and the tenant context would be empty.
4. **No `tenantId` parameter on any tool.** The tenant always comes from the token, never from the model. A model can't ask for another tenant's data because the tool has no way to take that input.
5. **Same rules as the matching controller.** Each tool has the same RBAC policy (`[Authorize(Policy = …)]` + `AddAuthorizationFilters()`) and the same entitlement check (`IEntitlementEnforcer`) as the controller action it mirrors. Tools stay transport-only: no `ApplicationDbContext`, only Application services.
6. **Read-only today.** No create, update, delete, rotate-secret or billing actions. No secrets in any output (the webhook view has no signing secret, which is why it's safe).
7. **Auth for Claude Code today: a bearer token in a header.** Full MCP OAuth (protected-resource metadata, an authorization server) is out of scope. It's a later step for a real remote deployment.

### The 4 tools

| Tool | Mirrors | Service | RBAC policy | Entitlement |
|---|---|---|---|---|
| `get_usage_summary(days = 30, action?)` | `GET /api/v1/tenant/analytics/usage` | `IUsageAnalyticsService.GetTenantUsageSummaryAsync(days, action, ct)` | `RbacPolicyNames.AuditLogsRead` | `EntitlementKeys.AnalyticsAuditLogsRead` |
| `list_audit_logs(page = 1, pageSize = 50, action?, fromUtc?, toUtc?)` | `GET /api/v1/tenant/audit-logs` | `IAuditService.GetTenantAuditLogsAsync(page, pageSize, action, fromUtc, toUtc)` | `RbacPolicyNames.AuditLogsRead` | `EntitlementKeys.AnalyticsAuditLogsRead` |
| `list_webhook_endpoints()` | `GET /api/v1/tenant/webhook-endpoints` | `IWebhookEndpointManagementService.ListEndpointsAsync(tenantId, ct)` | `RbacPolicyNames.BillingManage` | none (the controller's `List` action has none) |
| `list_cors_origins()` | `GET /api/v1/admin/tenant/cors-origins` | `ITenantCorsOriginService.ListAsync(tenantId, ct)` | `RbacPolicyNames.TenantsRead` | `EntitlementKeys.SecurityCorsOriginsManage` |

> Checked against the controllers and services on `master`:
> - **Limits.** No controller validates input. The services bound it: `UsageAnalyticsService` clamps `days` to 1–90; `AuditService` uses `page ≥ 1` and clamps `pageSize` to 1–200. The tools pass the values through unchanged (same behavior) and state the bounds in their descriptions.
> - **Cancellation.** `IAuditService.GetTenantAuditLogsAsync` takes no `CancellationToken` (nor does the controller action), so `list_audit_logs` can't pass one through. Don't add one to the service in this slice.
> - **Tenant scoping.** The usage and audit services read the tenant from `ITenantContext` themselves. The webhook and CORS services take `tenantId`: pass `ITenantContext.TenantId`.
> - **Entitlement check.** The controllers use `this.EnforceFeatureAsync(...)`, which extends `ControllerBase` and returns an `IActionResult`, so tools can't reuse it. Call `IEntitlementEnforcer.EnsureFeatureEnabledAsync(tenantId, key, ct)` and use `.Allowed` / `.DenialReason`.
> - **Outputs.** `WebhookEndpointManagementView` has no signing secret. Audit entries hold emails and IPs (already returned by the REST endpoint), no tokens or secrets.
> - **Plans.** Both seeded plans (Free, Pro) grant `feature.analytics.audit_logs.read` and `feature.security.cors_origins.manage`, so a new tenant can use all four tools.
> **Not today:** `get_billing_status`. `BillingController.GetStatus` reads `ApplicationDbContext` directly, so there's no Application service to wrap yet. Extracting one is a separate slice.

---

## Block 1: Read and plan (30 min)

- [x] Read the SDK pages: Transports, Stateless and stateful, Tools, Identity and roles (the links above)
- [x] Read `Presentation/Program.cs` pipeline order: `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` → `TenantMiddleware` → `RateLimitMiddleware` → endpoints. Note that `UseAuthorization` runs **before** `TenantMiddleware`: the endpoint-level `RequireAuthorization()` 401 happens before tenant resolution, while the tool-level `[Authorize(Policy)]` runs inside the MCP endpoint, after `TenantMiddleware`. Both are safe because `PermissionAuthorizationHandler` reads `tenant_id` from the JWT, not from `ITenantContext`.
- [x] Confirm in `TenantMiddleware` and `RateLimitMiddleware` that `/api/v1/mcp` is **not** skipped, so the tenant is resolved and the request is rate-limited
- [x] Open the 4 controller actions in the table and note their parameters, limits, policies and entitlement checks
- [x] Explain decision 3 (why stateless) in your own words before moving on

---

## Block 2: Add the MCP endpoint (45 min)

### 2.1 Package
```bash
dotnet add Presentation package ModelContextProtocol.AspNetCore --version 2.4.0
dotnet build
```
- [x] Build passes

### 2.2 Register and map
Keep `Program.cs` tidy with a registration extension, in the same style as `RbacAuthorizationRegistrationExtensions` (e.g. `Presentation/Mcp/McpServerRegistrationExtensions.cs` with `AddTenantMcpServer()` and `MapTenantMcp()`). The shape:
```csharp
// services
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
    .AddAuthorizationFilters()
    .WithTools<TenantReadTools>();

// endpoints, next to MapControllers()
app.MapMcp("/api/v1/mcp").RequireAuthorization();
```
- [x] `RequireAuthorization()`: a request with no valid JWT gets **401** at the HTTP level, before it reaches MCP
- [x] Start with an empty `TenantReadTools` class (or one trivial `whoami_tenant` tool that returns `ITenantContext.TenantId`, to check the scope; delete it before the end)
- [x] Build passes, existing tests still pass: `dotnet test --no-build`

---

## Block 3: The 4 tools (75–90 min)

File: `Presentation/Mcp/TenantReadTools.cs` (one class, or one class per area if you prefer; each class needs its own `WithTools<T>()` line).

Shape of one tool (fill in the real types from the services):
```csharp
[McpServerToolType]
internal sealed class TenantReadTools(
    ITenantContext tenantContext,
    IEntitlementEnforcer entitlementEnforcer,
    IUsageAnalyticsService usageAnalytics /* , ... */)
{
    [McpServerTool(Name = "get_usage_summary", ReadOnly = true)]
    [Description("…what it returns, for which period, and that it only covers the caller's tenant…")]
    [Authorize(Policy = RbacPolicyNames.AuditLogsRead)]
    public async Task<TenantUsageAnalyticsSummary> GetUsageSummary(
        [Description("…")] int days = 30,
        [Description("…")] string? action = null,
        CancellationToken cancellationToken = default)
    {
        // 1. same entitlement check as the controller; denied → tool error with the denial reason
        // 2. call the service (it scopes by ITenantContext)
        // 3. return the result
    }
}
```

- [x] **3.1** `get_usage_summary`
- [x] **3.2** `list_audit_logs`, with the controller's paging limits
- [x] **3.3** `list_webhook_endpoints`. Output is `WebhookEndpointManagementView` (no secret)
- [x] **3.4** `list_cors_origins`
- [x] **3.5** Entitlement denial: report it as a **tool error** (the model sees it and can explain it), not as an HTTP error: `throw new McpException(enforcement.DenialReason)`. Per the Tools page, an `McpException` thrown by a tool becomes a `CallToolResult` with `IsError = true` and its message; `McpProtocolException` would become a JSON-RPC error instead, and any other exception gives only a generic message.
- [x] **3.6** Descriptions: write each one for a model. Say what it returns, the units, the defaults and bounds, and that results are limited to the caller's tenant. Mark every tool `ReadOnly = true` (`McpServerToolAttribute.ReadOnly` exists in 2.4.0, next to `Destructive`, `Idempotent` and `OpenWorld`).
- [x] **3.7** No `tenantId` parameter anywhere; no `ApplicationDbContext` in the tool class
- [x] Build passes

---

## Block 4: Tests, then Claude Code (75–90 min)

### 4.1 Integration tests (`Tests/Integration/McpServerTests.cs`)
Use the existing `ApiWebApplicationFactory` (SQLite in-memory, always-allow rate limiter) and the SDK client: `McpClient.CreateAsync` over `new HttpClientTransport(options, httpClient, loggerFactory, ownsHttpClient)` (that constructor exists in 2.4.0), with the client from `SecurityTestHelpers.CreateHttpsClient(factory)` (HTTPS base address, because of `UseHttpsRedirection`). Fall back to raw JSON-RPC POSTs if the client is awkward. No new package is needed in `Tests`: the SDK comes through the `Presentation` project reference. Reuse `SecurityTestHelpers` to register tenants and get tokens.
- [ ] No token → **401**
- [ ] Admin token → `tools/list` returns exactly the 4 tools; none has a `tenantId`-like property in its `inputSchema`
- [ ] **Tenant isolation:** tenant A's token → `get_usage_summary`, `list_audit_logs`, `list_webhook_endpoints`, `list_cors_origins` return only tenant A's data, even when tenant B has data
- [ ] **RBAC:** a MEMBER user (`SecurityTestHelpers.CreateMemberAndLoginAsync`; MEMBER lacks `auditlogs.read`) doesn't see `get_usage_summary` / `list_audit_logs` in `tools/list`, and calling one directly fails. The SDK docs disagree on the exact failure shape (Identity page: JSON-RPC error; Filters page: `McpException("Access forbidden")`), so the test pins down what 2.4.0 actually returns.
- [ ] **Entitlement:** with a `TenantEntitlementOverride` setting `AnalyticsAuditLogsRead` to `"false"` (same pattern as `AuthorizationSecurityTests`), the tool returns an error result with the denial reason
- [ ] **Suspended tenant:** after `SecurityTestHelpers.SuspendTenantAsync`, the tenant's token gets **403 `TenantSuspended`** from `TenantMiddleware`, same as the REST API
- [ ] All tests pass: `dotnet test --no-build`

### 4.2 Run locally and get a token
```bash
dotnet run --project Presentation --launch-profile http
```
- [ ] API is up on `http://localhost:5238` (use the **http** profile: Claude Code may not trust the local HTTPS dev certificate). `scripts/dev.sh run` would serve the API on `http://localhost:5000` instead and also start BillingService, which isn't needed here. PostgreSQL and Redis must be reachable.
- [ ] Create two tenant admins (A and B). Locally the email-verification token can't be obtained: `IdentityNotificationService` doesn't log it and the database stores only its hash. So for each tenant:
  1. `POST /api/v1/auth/register` with `{ "companyName", "subdomain", "adminEmail", "adminPassword" }` → `202` with `tenantId`
  2. Local database only: activate it by hand, e.g. `UPDATE "Tenants" SET "Status" = 0 WHERE "Id" = '<tenantId>';` (`0` = `Active`, stored as an int) and `UPDATE "Users" SET "EmailVerifiedAt" = now() WHERE "TenantId" = '<tenantId>';`
  3. `POST /api/v1/auth/login` with `{ "email", "password" }` → copy `token` (expires after 60 min)
  (Delivering verification tokens locally is a separate slice.)

### 4.3 Connect Claude Code → D2
```bash
claude mcp add --transport http saas-api http://localhost:5238/api/v1/mcp \
  --header "Authorization: Bearer <token>"
```
- [ ] Local scope only. **Never** put a token in a committed `.mcp.json`.
- [ ] `claude` → `/mcp`: `saas-api` connected, 4 tools listed
- [ ] Prompts to try (approve each call):
  - *"How has my tenant's usage looked over the last 7 days?"*
  - *"Show the 10 most recent audit log entries."*
  - *"Which webhook endpoints are active, and which events do they subscribe to?"*
  - *"Which CORS origins are allowed for my tenant?"*
- [ ] Log in as a **second tenant**, re-add the server with that token (`claude mcp remove saas-api` first), ask the same questions: different data, nothing from tenant A
- [ ] Screenshot of Claude Code answering from your API ✅ **D2 done**

**Troubleshooting**
- 401 from `/mcp`: token missing, expired (60 min) or for another environment. Log in again and re-add.
- 400 `TenantNotFound` / 403 `TenantMismatch`: the request carries a tenant hint (`X-Tenant-ID`, subdomain) that disagrees with the token. Drop it.
- 429: the plan-aware rate limiter is working; each MCP request counts.
- Tool sees no tenant / wrong scope: check that the transport is **stateless** (decision 3).

---

## Block 5: Stretch (optional, 45 min)

- [ ] **Token refresh for Claude Code.** Instead of a fixed header, use a `headersHelper` script (local, not committed) that logs in and prints `{"Authorization": "Bearer …"}`, so the 60-minute expiry stops breaking the connection. See the Claude Code MCP docs.
- [ ] **Audit MCP calls.** Log each tool call through `IAuditService` (action like `mcp.tool.called`, tool name), so MCP use shows up in the tenant's audit log like any other client.
- [ ] Read the ProtectedMcpServer sample and write down what full MCP OAuth would need here (protected-resource metadata, an authorization server, audience checks). Notes only, kept outside this repo; no code.

---

## Block 6: Docs and PR (30 min)

Per `AGENTS.md`, docs change in the same PR:
- [ ] `docs/decisions.md`: ADR "MCP endpoint is an in-process, stateless transport in Presentation" (decisions 1–7)
- [ ] `docs/contracts.md`: the MCP endpoint (`/api/v1/mcp`, Streamable HTTP, bearer JWT, the 4 tools with inputs, policies, entitlements)
- [ ] `docs/architecture.md`: MCP as a presentation transport next to the controllers
- [ ] `README.md`: capability line + "Using the MCP endpoint from Claude Code" (the `claude mcp add` command, with a placeholder token)
- [ ] `docs/features.md`: add the MCP endpoint to the entry-point inventory and counts
- [ ] `docs/CHANGELOG.md` entry
- [ ] Run and report the AGENTS.md .NET validation commands and their results: `dotnet --info`, `/tmp/dotnet-tools/dotnet-ef --version` (or `DOTNET_EF_BIN`; report it if missing), `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build --verbosity normal`
- [ ] Commit, push, open the PR

---

## End of day
- [ ] Both deliverables checked
- [ ] Tick week 6 Tuesday in ai-labs' `CLAUDE.md` (outside this repo)
- [ ] Ask Claude for today's recap PDF (for OneDrive; written outside this repo)

> **Short on time?** Blocks 1 → 2 → 3 → 4, then a short Block 6 (ADR + README). Drop Block 5 first.