using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class TenantLifecycleAndResolutionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public TenantLifecycleAndResolutionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SuspendedTenant_ShouldBeBlocked_FromLoginRefreshAndProtectedEndpoints()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var email = $"suspended-{Guid.NewGuid():N}@example.com";
        var password = "Passw0rd!";
        var registered = await RegisterTenantWithSubdomainAsync(client, email, password, $"suspend-{Guid.NewGuid():N}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = await db.Tenants.SingleAsync(x => x.Id == registered.TenantId);
            tenant.Status = TenantStatus.Suspended;
            await db.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = registered.TenantId,
            refreshToken = registered.RefreshToken
        });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.Token);
        var protectedResponse = await client.GetAsync("/api/admin/tenant");

        protectedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await protectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("TenantSuspended");
    }

    [Fact]
    public async Task TenantResolution_HostSubdomain_ShouldOverrideHeaderAndJwtFallbacks()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var tenantA = await RegisterTenantWithSubdomainAsync(client, $"host-a-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"host-a-{Guid.NewGuid():N}");
        var tenantB = await RegisterTenantWithSubdomainAsync(client, $"host-b-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"host-b-{Guid.NewGuid():N}");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/tenant");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        request.Headers.Add("X-Tenant-ID", tenantB.TenantId.ToString());
        request.Headers.Host = $"{tenantA.Subdomain}.example.test";

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(tenantA.TenantId);
    }

    [Fact]
    public async Task AuthAndAdminFlows_ShouldEmitExpectedAuditActions()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var email = $"audit-auth-{Guid.NewGuid():N}@example.com";
        var password = "Passw0rd!";
        await RegisterTenantWithSubdomainAsync(client, email, password, $"audit-{Guid.NewGuid():N}");

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();

        var adminToken = loginBody.GetProperty("token").GetString()!;
        var tenantId = loginBody.GetProperty("tenantId").GetGuid();
        var refreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId, refreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedRefreshToken = rotated.GetProperty("refreshToken").GetString()!;

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { tenantId, refreshToken = rotatedRefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var addUser = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = $"audit-member-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        addUser.StatusCode.Should().Be(HttpStatusCode.Created);
        var addBody = await addUser.Content.ReadFromJsonAsync<JsonElement>();
        var userId = addBody.GetProperty("id").GetGuid();

        var changeRole = await client.PutAsJsonAsync($"/api/admin/tenant/users/{userId}/role", new { role = "ADMIN" });
        changeRole.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = db.AuditLogs
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.Action)
            .ToList();

        actions.Should().Contain("USER_LOGGED_IN");
        actions.Should().Contain("USER_TOKEN_REFRESHED");
        actions.Should().Contain("USER_LOGGED_OUT");
        actions.Should().Contain("TENANT_USER_ROLE_CHANGED");
    }

    private static async Task<(string Token, string RefreshToken, Guid TenantId, string Subdomain)> RegisterTenantWithSubdomainAsync(
        HttpClient client,
        string email,
        string password,
        string subdomain)
    {
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            companyName = $"Company {Guid.NewGuid():N}",
            subdomain,
            adminEmail = email,
            adminPassword = password
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("token").GetString()!,
            body.GetProperty("refreshToken").GetString()!,
            body.GetProperty("tenantId").GetGuid(),
            subdomain);
    }
}
