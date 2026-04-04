using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

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
        var response = await client.GetAsync("/api/admin/tenant/users");

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
        var response = await client.PostAsJsonAsync("/api/admin/tenant/users", new
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

        var addResponse = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = $"target-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var userId = (await addResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);

        var roleChange = await client.PutAsJsonAsync($"/api/admin/tenant/users/{userId}/role", new { role = "ADMIN" });
        var delete = await client.DeleteAsync($"/api/admin/tenant/users/{userId}");

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
        var unauthorized = await client.GetAsync("/api/tenant/audit-logs");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var forbidden = await client.GetAsync("/api/tenant/audit-logs");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminPositiveControl_CanManageUsers()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"authz-pos-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var create = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = $"ok-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
