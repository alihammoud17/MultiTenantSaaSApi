using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Domain.Authorization;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
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
        await using var mcp = await CreateMcpClientAsync(admin.Token);

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

        await using var mcp = await CreateMcpClientAsync(tenantA.Token);

        var auditLogs = await CallToolJsonAsync(mcp, "list_audit_logs", new() { ["pageSize"] = 200 });
        auditLogs.GetArrayLength().Should().BeGreaterThan(0, "tenant A's registration and login are audited");
        auditLogs.EnumerateArray().Select(e => e.GetProperty("tenantId").GetGuid()).Should().OnlyContain(id => id == tenantA.TenantId);

        var usage = await CallToolJsonAsync(mcp, "get_usage_summary");
        usage.GetProperty("tenantId").GetGuid().Should().Be(tenantA.TenantId);
        usage.GetProperty("totalEvents").GetInt32().Should().Be(auditLogs.GetArrayLength(), "the summary counts only A's audit rows");

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

        await using var mcp = await CreateMcpClientAsync(memberToken);

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

        await using var mcp = await CreateMcpClientAsync(admin.Token);

        // Not CallToolJsonAsync: that helper requires success.
        var result = await mcp.CallToolAsync("list_audit_logs", cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain(EntitlementKeys.AnalyticsAuditLogsRead);
    }

    // The token is still valid; TenantMiddleware rejects the suspended tenant before MCP runs, same as the REST API.
    [Fact]
    public async Task SuspendedTenant_ShouldBeRejectedWith403()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-suspended-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        using var suspend = await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = CreateRawToolsListRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenant.Token);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TenantSuspended");
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

    // Connects an MCP client to the in-process test server as the token's user.
    // The transport owns the HttpClient, so disposing the McpClient disposes both.
    private async Task<McpClient> CreateMcpClientAsync(string token)
    {
        var httpClient = SecurityTestHelpers.CreateHttpsClient(_factory);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
