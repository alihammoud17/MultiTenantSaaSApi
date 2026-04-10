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

    [Fact]
    public async Task InvitedUser_ShouldRequireVerificationBeforeLogin()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"invite-admin-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var invitedEmail = $"member-{Guid.NewGuid():N}@example.com";
        var inviteResponse = await client.PostAsJsonAsync("/api/auth/invites", new
        {
            email = invitedEmail,
            role = "MEMBER"
        });
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var invitePayload = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inviteToken = invitePayload.GetProperty("inviteToken").GetString();

        var acceptResponse = await client.PostAsJsonAsync("/api/auth/invites/accept", new
        {
            tenantId = admin.TenantId,
            inviteToken,
            password = "Passw0rd!"
        });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = invitedEmail,
            password = "Passw0rd!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Email is not verified");
    }

    [Fact]
    public async Task RevokeAllSessions_ShouldInvalidateOutstandingRefreshTokens()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var email = $"sessions-{Guid.NewGuid():N}@example.com";
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, email, "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Passw0rd!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondRefreshToken = loginPayload.GetProperty("refreshToken").GetString();

        var inventoryResponse = await client.GetAsync("/api/auth/sessions");
        inventoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await inventoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        sessions.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        var revokeResponse = await client.PostAsJsonAsync("/api/auth/sessions/revoke-all", new
        {
            tenantId = auth.TenantId
        });
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokePayload = await revokeResponse.Content.ReadFromJsonAsync<JsonElement>();
        revokePayload.GetProperty("revokedCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var refreshOne = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        var refreshTwo = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = secondRefreshToken });

        refreshOne.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var refreshOnePayload = await refreshOne.Content.ReadFromJsonAsync<JsonElement>();
        refreshOnePayload.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");

        refreshTwo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var refreshTwoPayload = await refreshTwo.Content.ReadFromJsonAsync<JsonElement>();
        refreshTwoPayload.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");
    }
}
