using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

// POST /api/v1/auth/mcp-token: exchanges an API access token for a short-lived token whose audience is the MCP
// endpoint. McpServerTests covers what the MCP endpoint accepts; this class covers issuance and the REST side.
public class McpTokenTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string McpTokenRoute = "/api/v1/auth/mcp-token";
    private const string WebhookEndpointsRoute = "/api/v1/tenant/webhook-endpoints";

    private readonly ApiWebApplicationFactory _factory = factory;

    [Fact]
    public async Task IssuedToken_ShouldCarryTheCallersClaims_TheMcpAudience_AndA15MinuteLifetime()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-token-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        using var response = await PostAsync(client, admin.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("token").GetString());

        jwt.Audiences.Should().Equal(ApiWebApplicationFactory.JwtMcpAudience);
        jwt.Issuer.Should().Be(ApiWebApplicationFactory.JwtIssuer);
        jwt.Subject.Should().Be(SecurityTestHelpers.GetUserId(admin.Token).ToString());
        jwt.Claims.Single(c => c.Type == "tenant_id").Value.Should().Be(admin.TenantId.ToString());
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        body.GetProperty("expiresAt").GetDateTime().Should().BeCloseTo(jwt.ValidTo, TimeSpan.FromSeconds(1),
            "the reported expiry is the token's exp claim");
        body.TryGetProperty("refreshToken", out _).Should().BeFalse("MCP tokens are not refreshed; clients exchange again");
    }

    [Fact]
    public async Task McpToken_OnRest_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-token-rest-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var mcpToken = await SecurityTestHelpers.GetMcpTokenAsync(client, admin.Token);

        // Control: the login token works on the same route.
        (await GetStatusAsync(client, admin.Token, WebhookEndpointsRoute)).Should().Be(HttpStatusCode.OK);
        (await GetStatusAsync(client, mcpToken, WebhookEndpointsRoute)).Should().Be(HttpStatusCode.Unauthorized);
    }

    // The exchange accepts API tokens only, so an MCP token cannot extend itself.
    [Fact]
    public async Task McpToken_ShouldNotBeExchangedForAnother()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-token-chain-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var mcpToken = await SecurityTestHelpers.GetMcpTokenAsync(client, admin.Token);

        using var response = await PostAsync(client, mcpToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NoToken_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        using var response = await PostAsync(client, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // TenantMiddleware checks the authenticated tenant on /auth routes, so a suspended tenant's still-valid
    // login token cannot be turned into an MCP token.
    [Fact]
    public async Task SuspendedTenant_ShouldGetNoMcpToken()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-token-suspended-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        using var suspend = await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await PostAsync(client, tenant.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TenantSuspended");
    }

    [Fact]
    public async Task Issuance_ShouldBeAuditedForTheCallersTenantAndUser()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"mcp-token-audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var userId = SecurityTestHelpers.GetUserId(admin.Token);

        await SecurityTestHelpers.GetMcpTokenAsync(client, admin.Token);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // AuditLog has a tenant query filter, and this test scope has no tenant context.
        var audit = db.AuditLogs.IgnoreQueryFilters().Single(a => a.TenantId == admin.TenantId && a.Action == "MCP_TOKEN_ISSUED");
        audit.UserId.Should().Be(userId);
        audit.EntityId.Should().Be(userId.ToString());
        audit.Changes.Should().Contain("ExpiresAt").And.NotContain("eyJ", "the token itself is never logged");
    }

    // Equal audiences would make each surface accept the other's tokens, so the API refuses to start.
    [Fact]
    public void McpAudienceEqualToApiAudience_ShouldStopStartup()
    {
        using var misconfigured = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:McpAudience"] = ApiWebApplicationFactory.JwtAudience
            })));

        var start = () => misconfigured.CreateClient();

        start.Should().Throw<Exception>().Where(e => e.ToString().Contains("Jwt:McpAudience must be set and differ from Jwt:Audience"));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpTokenRoute);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string token, string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}
