using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Domain.Authorization;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Presentation.Mcp;

namespace Tests.Integration;

public class McpServerTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string WebhookEndpointsRoute = "/api/v1/tenant/webhook-endpoints";
    private const string CorsOriginsRoute = "/api/v1/admin/tenant/cors-origins";
    private const string McpTokenRoute = "/api/v1/auth/mcp-token";

    private readonly ApiWebApplicationFactory _factory = factory;

    // Raw HTTP on purpose: the claim is that RequireAuthorization rejects the request before MCP runs.
    // An McpClient would fail inside CreateAsync, and its exception shape is an SDK detail.
    [Fact]
    public async Task NoToken_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        using var request = CreateRawToolsListRequest();

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // An X-Tenant-ID hint must not give an anonymous caller a tenant context (TenantMiddleware would accept the hint).
    [Fact]
    public async Task NoToken_WithTenantHint_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var victim = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-hint-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        using var request = CreateRawToolsListRequest();
        request.Headers.Add("X-Tenant-ID", victim.TenantId.ToString());

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Exact list: adding a tool must be a deliberate change here too. The schema check enforces
    // "no tenantId parameter" on the contract the model actually sees, not just in the code.
    [Fact]
    public async Task AdminToken_ToolsList_ShouldReturnExactlyTheFourReadOnlyTools_WithoutTenantInputs()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-tools-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        await using var mcp = await CreateMcpClientForAccessTokenAsync(admin.Token);

        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            "get_usage_summary", "list_audit_logs", "list_webhook_endpoints", "list_cors_origins");

        foreach (var tool in tools)
        {
            // Parenthesized so a missing Annotations object fails instead of skipping the assertion.
            (tool.ProtocolTool.Annotations?.ReadOnlyHint).Should().BeTrue($"{tool.Name} must be marked read-only");

            InputNames(tool).Should().NotContain(
                name => name.Contains("tenant", StringComparison.OrdinalIgnoreCase),
                $"{tool.Name} must not let the caller choose a tenant");
        }

        // Proves the schema check above reads real inputs rather than passing on empty lists.
        InputNames(tools.Single(t => t.Name == "list_audit_logs")).Should().Contain(["page", "pageSize"]);

        static List<string> InputNames(McpClientTool tool) =>
            tool.JsonSchema.TryGetProperty("properties", out var properties)
                ? properties.EnumerateObject().Select(p => p.Name).ToList()
                : [];
    }

    // Both tenants get data. If only B had data, "A sees nothing of B" would also pass when the tools see
    // no tenant at all (Guid.Empty from a wrong DI scope). Asserting A's own rows proves both properties.
    [Fact]
    public async Task TenantA_ShouldOnlySeeItsOwnData_AcrossAllFourTools()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-iso-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-iso-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        // Seed through the REST API so the data goes through the same tenant-scoped services as real callers.
        var originA = $"https://a-{Guid.NewGuid():N}.example.com";
        var originB = $"https://b-{Guid.NewGuid():N}.example.com";
        await PostAsAsync(client, tenantA.Token, WebhookEndpointsRoute, new { name = "a-hook", callbackUrl = "https://a.example.com/hook" });
        await PostAsAsync(client, tenantB.Token, WebhookEndpointsRoute, new { name = "b-hook", callbackUrl = "https://b.example.com/hook" });
        await PostAsAsync(client, tenantA.Token, CorsOriginsRoute, new { origin = originA });
        await PostAsAsync(client, tenantB.Token, CorsOriginsRoute, new { origin = originB });

        await using var mcp = await CreateMcpClientForAccessTokenAsync(tenantA.Token);

        var auditLogs = await CallToolJsonAsync(mcp, "list_audit_logs", new() { ["pageSize"] = 200 });
        auditLogs.GetArrayLength().Should().BeGreaterThan(0, "tenant A's registration and login are audited");
        auditLogs.EnumerateArray().Select(e => e.GetProperty("tenantId").GetGuid()).Should().OnlyContain(id => id == tenantA.TenantId);

        var usage = await CallToolJsonAsync(mcp, "get_usage_summary");
        usage.GetProperty("tenantId").GetGuid().Should().Be(tenantA.TenantId);
        usage.GetProperty("totalEvents").GetInt32().Should().Be(
            auditLogs.GetArrayLength() + 1, "the summary counts only A's audit rows, plus the audit entry of the list_audit_logs call itself");

        var webhooks = await CallToolJsonAsync(mcp, "list_webhook_endpoints");
        webhooks.EnumerateArray().Select(e => e.GetProperty("name").GetString()).Should().Equal("a-hook");

        var origins = await CallToolJsonAsync(mcp, "list_cors_origins");
        origins.EnumerateArray().Select(e => e.GetProperty("origin").GetString()).Should().Equal(originA);
    }

    [Fact]
    public async Task Member_ShouldNotSeeOrCallAuditTools()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-rbac-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);

        await using var mcp = await CreateMcpClientForAccessTokenAsync(memberToken);

        // RBAC filtering hides the audit tools (a MEMBER holds none of the four tools' permissions).
        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        tools.Select(t => t.Name).Should().NotContain(["get_usage_summary", "list_audit_logs"]);

        // Calling a hidden tool directly is refused as a JSON-RPC error, not as a tool result (observed in SDK 2.4.0):
        // McpProtocolException, InvalidRequest, "Access forbidden: This tool requires authorization."
        var call = () => mcp.CallToolAsync("list_audit_logs", cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var denial = await call.Should().ThrowAsync<McpProtocolException>();
        denial.Which.ErrorCode.Should().Be(McpErrorCode.InvalidRequest);
        denial.Which.Message.Should().Contain("Access forbidden");
    }

    // An entitlement denial is a tool result with IsError = true and the denial reason, so the model can explain it;
    // the REST endpoint returns 403 for the same tenant.
    [Fact]
    public async Task EntitlementDisabled_ShouldReturnToolErrorWithReason()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-entitlement-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
            {
                Id = Guid.NewGuid(),
                TenantId = admin.TenantId,
                EntitlementKey = EntitlementKeys.AnalyticsAuditLogsRead,
                Value = "false",
                Reason = "Analytics module disabled",
                Source = TenantEntitlementOverrideSource.ManualCorrection,
                EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-1),
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var mcp = await CreateMcpClientForAccessTokenAsync(admin.Token);

        // Not CallToolJsonAsync: that helper requires success.
        var result = await mcp.CallToolAsync("list_audit_logs", cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain(EntitlementKeys.AnalyticsAuditLogsRead);

        // The denied call still passed RBAC, so it is audited, marked as an error.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // AuditLog has a tenant query filter, and this test scope has no tenant context (same as ApiEndpointsTests).
            var audit = db.AuditLogs.IgnoreQueryFilters()
                .Single(a => a.TenantId == admin.TenantId && a.Action == McpToolCallAuditFilter.AuditAction);
            audit.EntityId.Should().Be("list_audit_logs");
            audit.Changes.Should().Contain("\"isError\":true");
        }
    }

    [Fact]
    public async Task ToolCalls_ShouldBeAuditedForTheCallersTenant()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        await using var mcp = await CreateMcpClientForAccessTokenAsync(admin.Token);

        await CallToolJsonAsync(mcp, "list_webhook_endpoints");
        var entries = await CallToolJsonAsync(mcp, "list_audit_logs", new() { ["action"] = McpToolCallAuditFilter.AuditAction });

        // Newest first: this list_audit_logs call is audited only after it returns, so it isn't in its own result.
        var entry = entries.EnumerateArray().Should().ContainSingle().Subject;
        entry.GetProperty("tenantId").GetGuid().Should().Be(admin.TenantId);
        entry.GetProperty("entityType").GetString().Should().Be(McpToolCallAuditFilter.AuditEntityType);
        entry.GetProperty("entityId").GetString().Should().Be("list_webhook_endpoints");
        entry.GetProperty("userId").GetGuid().Should().NotBe(Guid.Empty, "the acting user comes from the token");
        entry.GetProperty("changes").GetString().Should().Be("{\"isError\":false}");
    }

    // The MCP token is still valid; TenantMiddleware rejects the suspended tenant before MCP runs, same as the REST API.
    [Fact]
    public async Task SuspendedTenant_ShouldBeRejectedWith403()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-suspended-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        // Exchanged before the suspension: afterwards /auth/mcp-token refuses the tenant too.
        var mcpToken = await SecurityTestHelpers.GetMcpTokenAsync(client, tenant.Token);
        using var suspend = await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = CreateRawToolsListRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcpToken);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TenantSuspended");
    }

    // Positive control for the hand-minted tokens below: without it, the 401 tests would also pass
    // if CreateTestJwt produced tokens the server could never accept.
    [Fact]
    public async Task MintedValidToken_ShouldBeAccepted()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-minted-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var token = MintMcpToken(SecurityTestHelpers.GetUserId(admin.Token), admin.TenantId, DateTime.UtcNow.AddMinutes(10));

        using var response = await SendRawToolsListAsync(client, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Spec (2026-07-28): invalid or expired tokens MUST get 401, and tokens must be issued for this server (audience).
    // Each case changes one property of an otherwise valid MCP token; "api-audience" is the caller's real login token,
    // which REST accepts. Expired is 10 minutes old because JwtBearer accepts 5 minutes of clock skew by default.
    [Theory]
    [InlineData("api-audience")]
    [InlineData("expired")]
    [InlineData("wrong-signing-key")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    public async Task InvalidToken_ShouldReturn401WithBearerInvalidTokenChallenge(string defect)
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-invalid-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var userId = SecurityTestHelpers.GetUserId(admin.Token);
        var validUntil = DateTime.UtcNow.AddMinutes(10);

        var token = defect switch
        {
            "api-audience" => admin.Token,
            "expired" => MintMcpToken(userId, admin.TenantId, DateTime.UtcNow.AddMinutes(-10)),
            // At least 32 bytes, so HS256 signing succeeds and only validation can fail.
            "wrong-signing-key" => MintMcpToken(userId, admin.TenantId, validUntil, signingKey: "a_different_signing_key_also_32_bytes_or_more"),
            "wrong-audience" => MintMcpToken(userId, admin.TenantId, validUntil, audience: "https://other-resource.example.com"),
            "wrong-issuer" => MintMcpToken(userId, admin.TenantId, validUntil, issuer: "https://other-issuer.example.com"),
            _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, null)
        };

        using var response = await SendRawToolsListAsync(client, token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = response.Headers.WwwAuthenticate.Should().ContainSingle().Subject;
        challenge.Scheme.Should().Be("Bearer");
        challenge.Parameter.Should().Contain("error=\"invalid_token\"");
        // No MCP OAuth discovery yet (open item 13). Adding Protected Resource Metadata must change this on purpose.
        challenge.Parameter.Should().NotContain("resource_metadata");
    }

    // A validly signed token without tenant_id. JwtService never issues one, so this needs the signing key.
    [Fact]
    public async Task TokenWithoutTenantClaim_WithoutHint_ShouldReturn400TenantNotFound()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-notenant-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var token = MintMcpToken(SecurityTestHelpers.GetUserId(admin.Token), tenantId: null, DateTime.UtcNow.AddMinutes(10));

        using var response = await SendRawToolsListAsync(client, token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TenantNotFound");
    }

    // TenantMiddleware falls back to the hint when an authenticated token has no usable tenant_id (pinned in
    // TenantMiddlewareTests), so this request reaches the MCP handler with the victim's tenant set. What keeps the tools
    // closed is PermissionAuthorizationHandler, which needs a tenant_id claim: every tool has an RBAC policy, so all are
    // hidden and refused. A tool without a policy would run here (ADR follow-up). Tenant B has data so a leak would show.
    [Fact]
    public async Task TokenWithoutTenantClaim_WithTenantHint_ShouldReachNoTool()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var attacker = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-notenant-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var victim = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-notenant-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        await PostAsAsync(client, victim.Token, WebhookEndpointsRoute, new { name = "victim-hook", callbackUrl = "https://victim.example.com/hook" });
        var token = MintMcpToken(SecurityTestHelpers.GetUserId(attacker.Token), tenantId: null, DateTime.UtcNow.AddMinutes(10));

        await using var mcp = await CreateMcpClientAsync(token, tenantHint: victim.TenantId);

        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        tools.Should().BeEmpty("no RBAC policy succeeds without a tenant_id claim");
        await AssertToolCallForbiddenAsync(mcp, "list_webhook_endpoints");
        await AssertToolCallForbiddenAsync(mcp, "list_audit_logs");
    }

    [Fact]
    public async Task TokenForTenantA_WithTenantBHint_ShouldReturn403TenantMismatch()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-mismatch-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-mismatch-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var mcpTokenA = await SecurityTestHelpers.GetMcpTokenAsync(client, tenantA.Token);

        using var response = await SendRawToolsListAsync(client, mcpTokenA, tenantHint: tenantB.TenantId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TenantMismatch");
    }

    // The model chooses tool arguments, so it could try to name another tenant. No tool declares a tenant input
    // (see the schema test); this pins what the server does with one anyway: the SDK ignores unknown arguments
    // (observed in 2.4.0) and the tools still read the tenant from the token.
    [Fact]
    public async Task SmuggledTenantArgument_ShouldBeIgnored_AndReturnOnlyTheCallersData()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-smuggle-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-smuggle-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        await PostAsAsync(client, tenantA.Token, WebhookEndpointsRoute, new { name = "a-hook", callbackUrl = "https://a.example.com/hook" });
        await PostAsAsync(client, tenantB.Token, WebhookEndpointsRoute, new { name = "b-hook", callbackUrl = "https://b.example.com/hook" });
        var smuggled = tenantB.TenantId.ToString();

        await using var mcp = await CreateMcpClientForAccessTokenAsync(tenantA.Token);

        // A tool without parameters.
        var webhooks = await CallToolJsonAsync(mcp, "list_webhook_endpoints", new() { ["tenantId"] = smuggled, ["tenant_id"] = smuggled });
        webhooks.EnumerateArray().Select(e => e.GetProperty("name").GetString()).Should().Equal("a-hook");

        // A tool with parameters: the smuggled ones sit next to valid input.
        var auditLogs = await CallToolJsonAsync(mcp, "list_audit_logs", new() { ["pageSize"] = 200, ["tenantId"] = smuggled, ["tenant_id"] = smuggled });
        auditLogs.GetArrayLength().Should().BeGreaterThan(0);
        auditLogs.EnumerateArray().Select(e => e.GetProperty("tenantId").GetGuid()).Should().OnlyContain(id => id == tenantA.TenantId);
    }

    // There is no user deactivation, only hard delete. The JWTs stay valid until they expire, but every RBAC policy looks
    // the user up per request, so REST and MCP both refuse a deleted user on policy-gated actions. Actions with only
    // [Authorize] would still accept the token (known limit of stateless JWTs); the MCP token's 15-minute lifetime bounds it.
    [Fact]
    public async Task DeletedUser_ShouldBeRefusedOnMcp_LikeOnRest()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-deleted-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        // Before the deletion the tokens work on both surfaces, so the refusals below come from the deletion.
        var mcpToken = await SecurityTestHelpers.GetMcpTokenAsync(client, admin.Token);
        (await GetAsAsync(client, admin.Token, WebhookEndpointsRoute)).Should().Be(HttpStatusCode.OK);
        await using (var before = await CreateMcpClientAsync(mcpToken))
        {
            var toolsBefore = await before.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            toolsBefore.Select(t => t.Name).Should().Contain("list_webhook_endpoints");
        }

        // Directly in the database: the REST delete needs step-up and an entitlement, which aren't under test here.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = SecurityTestHelpers.GetUserId(admin.Token);
            db.Users.Remove(db.Users.IgnoreQueryFilters().Single(u => u.Id == userId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await GetAsAsync(client, admin.Token, WebhookEndpointsRoute)).Should().Be(HttpStatusCode.Forbidden);
        (await PostAsStatusAsync(client, admin.Token, McpTokenRoute)).Should().Be(HttpStatusCode.Unauthorized, "a deleted user gets no new MCP token");

        // The MCP token issued before still authenticates (the handshake succeeds), but no tool is visible or callable.
        await using var mcp = await CreateMcpClientAsync(mcpToken);
        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        tools.Should().BeEmpty();
        await AssertToolCallForbiddenAsync(mcp, "list_webhook_endpoints");
    }

    // The RBAC denial shape pinned in Member_ShouldNotSeeOrCallAuditTools (SDK 2.4.0).
    private static async Task AssertToolCallForbiddenAsync(McpClient mcp, string toolName)
    {
        var call = () => mcp.CallToolAsync(toolName, cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var denial = await call.Should().ThrowAsync<McpProtocolException>();
        denial.Which.ErrorCode.Should().Be(McpErrorCode.InvalidRequest);
        denial.Which.Message.Should().Contain("Access forbidden");
    }

    private static async Task<HttpResponseMessage> SendRawToolsListAsync(HttpClient client, string token, Guid? tenantHint = null)
    {
        using var request = CreateRawToolsListRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenantHint is not null)
        {
            request.Headers.Add("X-Tenant-ID", tenantHint.Value.ToString());
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // A token minted with the test signing settings; the audience defaults to the MCP resource.
    private static string MintMcpToken(
        Guid userId,
        Guid? tenantId,
        DateTime expiresUtc,
        string signingKey = ApiWebApplicationFactory.JwtSecret,
        string issuer = ApiWebApplicationFactory.JwtIssuer,
        string audience = ApiWebApplicationFactory.JwtMcpAudience)
        => SecurityTestHelpers.CreateTestJwt(userId, tenantId, expiresUtc, signingKey: signingKey, issuer: issuer, audience: audience);

    private static async Task<HttpStatusCode> PostAsStatusAsync(HttpClient client, string token, string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> GetAsAsync(HttpClient client, string token, string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    // Calls a tool, requires a successful (non-error) result, and returns the JSON from its text content.
    private static async Task<JsonElement> CallToolJsonAsync(McpClient mcp, string toolName, Dictionary<string, object?>? arguments = null)
    {
        var result = await mcp.CallToolAsync(toolName, arguments, cancellationToken: TestContext.Current.CancellationToken);

        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        (result.IsError ?? false).Should().BeFalse($"{toolName} should succeed, but returned: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task PostAsAsync(HttpClient client, string token, string route, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"seeding {route} should succeed");
    }

    private static HttpRequestMessage CreateRawToolsListRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpServerRegistrationExtensions.RoutePattern)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        return request;
    }

    // Exchanges an API access token for an MCP token, then connects with it.
    private async Task<McpClient> CreateMcpClientForAccessTokenAsync(string accessToken)
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        return await CreateMcpClientAsync(await SecurityTestHelpers.GetMcpTokenAsync(client, accessToken));
    }

    // Connects an MCP client to the in-process test server with an MCP token, optionally sending an X-Tenant-ID hint.
    // The transport owns the HttpClient, so disposing the McpClient disposes both.
    private async Task<McpClient> CreateMcpClientAsync(string token, Guid? tenantHint = null)
    {
        var httpClient = SecurityTestHelpers.CreateHttpsClient(_factory);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenantHint is not null)
        {
            httpClient.DefaultRequestHeaders.Add("X-Tenant-ID", tenantHint.Value.ToString());
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, McpServerRegistrationExtensions.RoutePattern),
                // AutoDetect could fall back to legacy SSE, which the stateless server doesn't serve.
                TransportMode = HttpTransportMode.StreamableHttp
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }
}
