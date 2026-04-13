using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Authorization;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class AuthorizationSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthorizationSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MemberCannotListTenantUsers_ShouldReturn403()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-admin-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var response = await client.GetAsync("/api/v1/admin/tenant/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MemberCannotCreateTenantUsers_ShouldReturn403()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-create-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var response = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"blocked-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MemberCannotChangeRoleOrDeleteUser_ShouldReturn403()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-role-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var addResponse = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"target-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var userId = (await addResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);

        var roleChange = await client.PutAsJsonAsync($"/api/v1/admin/tenant/users/{userId}/role", new { role = "ADMIN" });
        var delete = await client.DeleteAsync($"/api/v1/admin/tenant/users/{userId}");

        roleChange.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MemberCannotReadAuditLogs_ShouldReturn403_AndMissingTokenShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = null;
        var unauthorized = await client.GetAsync("/api/v1/tenant/audit-logs");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var forbidden = await client.GetAsync("/api/v1/tenant/audit-logs");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminPositiveControl_CanManageUsers()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-pos-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var create = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"ok-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AdminUserManagement_ShouldReturnForbidden_WhenAdvancedAdminEntitlementDisabled()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-admin-entitlement-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
            {
                Id = Guid.NewGuid(),
                TenantId = admin.TenantId,
                EntitlementKey = EntitlementKeys.AdminAdvancedUserManagement,
                Value = "false",
                Reason = "Advanced admin module disabled",
                Source = TenantEntitlementOverrideSource.ManualCorrection,
                EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-1),
                CreatedUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var response = await client.GetAsync("/api/v1/admin/tenant/users");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuditLogs_ShouldReturnForbidden_WhenAnalyticsEntitlementDisabled()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-analytics-entitlement-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

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
            db.SaveChanges();
        }

        var response = await client.GetAsync("/api/v1/tenant/audit-logs");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MemberCannotReadUsageAnalytics_ShouldReturn403_AndMissingTokenShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-usage-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = null;
        var unauthorized = await client.GetAsync("/api/v1/tenant/analytics/usage");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var forbidden = await client.GetAsync("/api/v1/tenant/analytics/usage");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
