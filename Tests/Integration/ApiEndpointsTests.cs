using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class ApiEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ApiEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ShouldReturnOk()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_ShouldCreateTenantAndReturnToken()
    {
        using var client = CreateClient();

        var payload = new
        {
            companyName = $"Acme {Guid.NewGuid():N}",
            subdomain = $"acme-{Guid.NewGuid():N}",
            adminEmail = $"admin-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        };

        var response = await client.PostAsJsonAsync("/api/auth/register", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("tenantId").GetGuid().Should().NotBe(Guid.Empty);
        body.GetProperty("userId").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Login_ShouldReturnJwt_ForExistingUser()
    {
        using var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var password = "Passw0rd!";

        await RegisterTenant(client, email, password);

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetPlans_ShouldReturnAvailablePlans_WithoutAuthOrTenantHeaders()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await response.Content.ReadFromJsonAsync<JsonElement>();

        plans.ValueKind.Should().Be(JsonValueKind.Array);
        plans.EnumerateArray().Select(x => x.GetProperty("id").GetString()).Should().Contain(["plan-free", "plan-pro"]);
    }

    [Fact]
    public async Task UpgradePlan_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-pro" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpgradePlan_ShouldSwitchTenantToProPlan_UsingJwtTenantClaim()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"up-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-pro" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = db.Subscriptions.Single(x => x.TenantId == auth.TenantId);
        subscription.PlanId.Should().Be("plan-pro");
    }

    [Fact]
    public async Task AuditLogs_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/tenant/audit-logs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuditLogs_ShouldReturnTenantPlanChangedEvent()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var upgrade = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-pro" });
        upgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync("/api/tenant/audit-logs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await response.Content.ReadFromJsonAsync<JsonElement>();
        logs.ValueKind.Should().Be(JsonValueKind.Array);
        logs.EnumerateArray()
            .Select(x => x.GetProperty("action").GetString())
            .Should()
            .Contain("TENANT_PLAN_CHANGED");
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static async Task<(string Token, Guid TenantId)> RegisterTenant(HttpClient client, string email, string password)
    {
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            companyName = $"Company {Guid.NewGuid():N}",
            subdomain = $"tenant-{Guid.NewGuid():N}",
            adminEmail = email,
            adminPassword = password
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var tenantId = body.GetProperty("tenantId").GetGuid();

        return (token, tenantId);
    }
}
