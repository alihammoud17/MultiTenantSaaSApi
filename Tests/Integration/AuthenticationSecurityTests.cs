using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Tests.Integration;

public class AuthenticationSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthenticationSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MissingAuthHeader_ShouldReturn401_OnProtectedEndpoint()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var response = await client.GetAsync("/api/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MalformedBearerToken_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bad.token.value");

        var response = await client.GetAsync("/api/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidToken_WithTenantHeaderOverride_ShouldResolveToHeaderTenantContext()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"auth-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"auth-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantB.TenantId.ToString());

        var response = await client.GetAsync("/api/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(tenantB.TenantId);
    }

    [Fact]
    public async Task StaleRotatedRefreshToken_ShouldFail()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"stale-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var rotate = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);

        var staleUse = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });

        staleUse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutThenRefreshSameToken_ShouldFail()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"logout-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidRevokePayload_ShouldReturnValidationFailure()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"revoke-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/auth/revoke", new { tenantId = Guid.Empty, refreshToken = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("TenantId is required");
    }
}
