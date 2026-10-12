using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            tenantId = registered.TenantId,
            refreshToken = registered.RefreshToken
        });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.Token);
        var protectedResponse = await client.GetAsync("/api/v1/admin/tenant");

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

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/tenant");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        request.Headers.Add("X-Tenant-ID", tenantB.TenantId.ToString());
        request.Headers.Host = $"{tenantA.Subdomain}.example.test";

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(tenantA.TenantId);
    }

    [Fact]
    public async Task TenantResolution_ReservedHostLabel_ShouldNotCaptureOtherTenantsRequests()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        // A tenant holding "api" (e.g. registered before reserved names were rejected) must not own api.<BaseDomain>.
        await EnsureTenantWithSubdomainAsync("api");
        var victim = await RegisterTenantWithSubdomainAsync(client, $"victim-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"victim-{Guid.NewGuid():N}");

        var response = await SendAdminTenantRequestAsync(client, $"api.{ApiWebApplicationFactory.TenantBaseDomain}", victim.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(victim.TenantId);
    }

    [Theory]
    [InlineData("{0}.other.test")]          // different domain
    [InlineData("{0}.deep.example.test")]   // more than one label under the base domain
    [InlineData("{0}")]                     // bare label, no domain
    public async Task TenantResolution_HostOutsideBaseDomain_ShouldNotResolveTenant(string hostTemplate)
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var attacker = await RegisterTenantWithSubdomainAsync(client, $"attacker-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"attacker-{Guid.NewGuid():N}");
        var victim = await RegisterTenantWithSubdomainAsync(client, $"victim-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"victim-{Guid.NewGuid():N}");

        var response = await SendAdminTenantRequestAsync(client, string.Format(hostTemplate, attacker.Subdomain), victim.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(victim.TenantId);
    }

    [Theory]
    [InlineData("127.0.0.1", "127")]
    [InlineData("localhost", "localhost")]
    public async Task TenantResolution_IpAndLocalhostHosts_ShouldNotResolveTenant(string host, string matchingSubdomain)
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        await EnsureTenantWithSubdomainAsync(matchingSubdomain);
        var victim = await RegisterTenantWithSubdomainAsync(client, $"victim-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"victim-{Guid.NewGuid():N}");

        var response = await SendAdminTenantRequestAsync(client, host, victim.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(victim.TenantId);
    }

    [Fact]
    public async Task TenantResolution_WithoutBaseDomain_ShouldDisableHostResolution()
    {
        using var registrationClient = SecurityTestHelpers.CreateHttpsClient(_factory);
        var hostTenant = await RegisterTenantWithSubdomainAsync(registrationClient, $"no-base-a-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"no-base-a-{Guid.NewGuid():N}");
        var caller = await RegisterTenantWithSubdomainAsync(registrationClient, $"no-base-b-{Guid.NewGuid():N}@example.com", "Passw0rd!", $"no-base-b-{Guid.NewGuid():N}");
        var host = $"{hostTenant.Subdomain}.{ApiWebApplicationFactory.TenantBaseDomain}";

        // With a base domain, the host resolves to hostTenant and conflicts with the caller's JWT.
        var withBaseDomain = await SendAdminTenantRequestAsync(registrationClient, host, caller.Token);
        withBaseDomain.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var noBaseDomainFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["TenantResolution:BaseDomain"] = "" })));
        using var client = noBaseDomainFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // Without one, the host is ignored and the caller's JWT tenant is used.
        var withoutBaseDomain = await SendAdminTenantRequestAsync(client, host, caller.Token);

        withoutBaseDomain.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await withoutBaseDomain.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(caller.TenantId);
    }

    [Fact]
    public async Task AuthAndAdminFlows_ShouldEmitExpectedAuditActions()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var email = $"audit-auth-{Guid.NewGuid():N}@example.com";
        var password = "Passw0rd!";
        await RegisterTenantWithSubdomainAsync(client, email, password, $"audit-{Guid.NewGuid():N}");

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();

        var adminToken = loginBody.GetProperty("token").GetString()!;
        var tenantId = loginBody.GetProperty("tenantId").GetGuid();
        var refreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId, refreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedRefreshToken = rotated.GetProperty("refreshToken").GetString()!;

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { tenantId, refreshToken = rotatedRefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var addUser = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"audit-member-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        addUser.StatusCode.Should().Be(HttpStatusCode.Created);
        var addBody = await addUser.Content.ReadFromJsonAsync<JsonElement>();
        var userId = addBody.GetProperty("id").GetGuid();

        var changeRole = await client.PutAsJsonAsync($"/api/v1/admin/tenant/users/{userId}/role", new { role = "ADMIN" });
        changeRole.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = db.AuditLogs
            .IgnoreQueryFilters()
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
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
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

    private static async Task<HttpResponseMessage> SendAdminTenantRequestAsync(HttpClient client, string host, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/tenant");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Host = host;

        return await client.SendAsync(request);
    }

    // Inserts a tenant directly, bypassing registration validation, to simulate data that predates it.
    private async Task EnsureTenantWithSubdomainAsync(string subdomain)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await dbContext.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return;

        var now = DateTime.UtcNow;
        dbContext.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = $"Legacy {subdomain}",
            Subdomain = subdomain,
            Status = TenantStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }
}
