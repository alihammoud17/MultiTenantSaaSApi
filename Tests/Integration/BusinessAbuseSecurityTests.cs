using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class BusinessAbuseSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public BusinessAbuseSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReusingOldRefreshTokenAfterRotation_ShouldFail()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"abuse-rotate-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var rotate = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);

        var oldReuse = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        oldReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokingSameTokenTwice_ShouldFailSecondTime()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"abuse-revoke-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var first = await client.PostAsJsonAsync("/api/auth/revoke", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken, reason = "TEST" });
        var second = await client.PostAsJsonAsync("/api/auth/revoke", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken, reason = "TEST" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutThenRefreshAgain_ShouldFail()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"abuse-logout-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RepeatedUpgradeToSamePlan_ShouldFailSafely()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"abuse-plan-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var first = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-pro" });
        var replay = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-pro" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeletedUserCannotContinueAuthFlow_RefreshFails()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"abuse-delete-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = db.Users.Single(x => x.TenantId == auth.TenantId && x.Role == "ADMIN");
            db.Users.Remove(user);
            db.SaveChanges();
        }

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid refresh token context");
    }
}
