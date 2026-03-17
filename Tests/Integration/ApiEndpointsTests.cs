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
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
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
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
    }


    [Fact]
    public async Task Refresh_ShouldIssueNewAccessAndRefreshTokens_ForValidRefreshToken()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"refresh-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("refreshToken").GetString().Should().NotBe(auth.RefreshToken);
    }

    [Fact]
    public async Task Refresh_ShouldReturnUnauthorized_WhenTenantDoesNotMatchToken()
    {
        using var client = CreateClient();

        var authA = await RegisterTenant(client, $"refresh-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var authB = await RegisterTenant(client, $"refresh-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = authB.TenantId,
            refreshToken = authA.RefreshToken
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");
    }

    [Fact]
    public async Task Refresh_ShouldReturnUnauthorized_ForInvalidRefreshToken()
    {
        using var client = CreateClient();
        var auth = await RegisterTenant(client, $"refresh-invalid-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = "not-a-valid-token"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");
    }

    [Fact]
    public async Task Refresh_ShouldReturnUnauthorized_ForRevokedRefreshToken()
    {
        using var client = CreateClient();
        var auth = await RegisterTenant(client, $"refresh-revoked-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var revokeResponse = await client.PostAsJsonAsync("/api/auth/revoke", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken,
            reason = "TEST_REVOKE"
        });

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");
    }

    [Fact]
    public async Task Refresh_ShouldRotateToken_AndInvalidatePreviousRefreshToken()
    {
        using var client = CreateClient();
        var auth = await RegisterTenant(client, $"refresh-rotate-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstRefresh.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedRefreshToken = firstBody.GetProperty("refreshToken").GetString();
        rotatedRefreshToken.Should().NotBeNullOrWhiteSpace();
        rotatedRefreshToken.Should().NotBe(auth.RefreshToken);

        var oldTokenRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        oldTokenRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var rotatedTokenRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = rotatedRefreshToken
        });

        rotatedTokenRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_ShouldRevokeRefreshToken_AndBlockFutureRefresh()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"logout-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }


    [Fact]
    public async Task Revoke_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"revoke-no-auth-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var revokeResponse = await client.PostAsJsonAsync("/api/auth/revoke", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken,
            reason = "SECURITY_EVENT"
        });

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_ShouldRevokeRefreshToken()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"revoke-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var revokeResponse = await client.PostAsJsonAsync("/api/auth/revoke", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken,
            reason = "SECURITY_EVENT"
        });

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoke_ShouldReturnUnauthorized_WhenTenantDoesNotMatchToken()
    {
        using var client = CreateClient();

        var authA = await RegisterTenant(client, $"revoke-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var authB = await RegisterTenant(client, $"revoke-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA.Token);

        var revokeResponse = await client.PostAsJsonAsync("/api/auth/revoke", new
        {
            tenantId = authB.TenantId,
            refreshToken = authA.RefreshToken,
            reason = "CROSS_TENANT_TEST"
        });

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await revokeResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");
    }

    [Fact]
    public async Task Register_ShouldReturnBadRequest_WhenSubdomainAlreadyExists()
    {
        using var client = CreateClient();
        var subdomain = $"dup-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/auth/register", new
        {
            companyName = "First Company",
            subdomain,
            adminEmail = $"first-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync("/api/auth/register", new
        {
            companyName = "Second Company",
            subdomain,
            adminEmail = $"second-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Subdomain already taken");
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_ForInvalidPassword()
    {
        using var client = CreateClient();
        var email = $"wrong-pass-{Guid.NewGuid():N}@example.com";

        await RegisterTenant(client, email, "Passw0rd!");

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "WrongPass123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid credentials");
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
    public async Task UpgradePlan_ShouldReturnBadRequest_WhenPlanIdMissing()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"missing-plan-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("PlanId is required");
    }

    [Fact]
    public async Task UpgradePlan_ShouldReturnBadRequest_WhenInvalidPlanId()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"invalid-plan-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-does-not-exist" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid plan id");
    }

    [Fact]
    public async Task UpgradePlan_ShouldReturnBadRequest_WhenAlreadyOnRequestedPlan()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"same-plan-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-free" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Tenant is already on this plan");
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

    [Fact]
    public async Task AuditLogs_ShouldSupportActionFilter()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"audit-filter-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var upgrade = await client.PostAsJsonAsync("/api/plans/upgrade", new { planId = "plan-pro" });
        upgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync("/api/tenant/audit-logs?action=TENANT_PLAN_CHANGED");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<JsonElement>();
        logs.ValueKind.Should().Be(JsonValueKind.Array);
        logs.GetArrayLength().Should().BeGreaterThan(0);
        logs.EnumerateArray()
            .Select(x => x.GetProperty("action").GetString())
            .Should()
            .OnlyContain(x => x == "TENANT_PLAN_CHANGED");
    }

    [Fact]
    public async Task AdminTenantDetails_ShouldReturnCurrentTenantData()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"admin-details-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.GetAsync("/api/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(auth.TenantId);
        body.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AdminListUsers_ShouldBeTenantScoped()
    {
        using var client = CreateClient();

        var tenantAEmail = $"admin-users-a-{Guid.NewGuid():N}@example.com";
        var tenantBEmail = $"admin-users-b-{Guid.NewGuid():N}@example.com";
        var tenantA = await RegisterTenant(client, tenantAEmail, "Passw0rd!");
        var tenantB = await RegisterTenant(client, tenantBEmail, "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        var addUser = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = $"member-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        addUser.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.GetAsync("/api/admin/tenant/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().Be(2);
        body.EnumerateArray()
            .Select(x => x.GetProperty("email").GetString())
            .Should()
            .Contain(email => email == tenantAEmail)
            .And.NotContain(email => email == tenantBEmail);
    }

    [Fact]
    public async Task AdminUserManagement_ShouldAddChangeRoleAndRemoveUser()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"admin-manage-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var memberEmail = $"managed-{Guid.NewGuid():N}@example.com";
        var addResponse = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = memberEmail,
            password = "Passw0rd!",
            role = "MEMBER"
        });

        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = addBody.GetProperty("id").GetGuid();

        var roleResponse = await client.PatchAsJsonAsync($"/api/admin/tenant/users/{userId}/role", new
        {
            role = "ADMIN"
        });

        roleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var roleBody = await roleResponse.Content.ReadFromJsonAsync<JsonElement>();
        roleBody.GetProperty("role").GetString().Should().Be("ADMIN");

        var removeResponse = await client.DeleteAsync($"/api/admin/tenant/users/{userId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var usersResponse = await client.GetAsync("/api/admin/tenant/users");
        var usersBody = await usersResponse.Content.ReadFromJsonAsync<JsonElement>();
        usersBody.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().NotContain(userId);
    }

    [Fact]
    public async Task AdminEndpoints_ShouldReturnForbidden_ForNonAdminUser()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"admin-rbac-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var memberEmail = $"rbac-member-{Guid.NewGuid():N}@example.com";
        var addResponse = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = memberEmail,
            password = "Passw0rd!",
            role = "MEMBER"
        });
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = memberEmail,
            password = "Passw0rd!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var memberToken = loginBody.GetProperty("token").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);

        var usersResponse = await client.GetAsync("/api/admin/tenant/users");
        usersResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminAuditLogs_ShouldReturnTenantScopedLogs()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"admin-audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var addUser = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = $"audit-member-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        addUser.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.GetAsync("/api/admin/tenant/audit-logs?action=TENANT_USER_ADDED");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<JsonElement>();
        logs.ValueKind.Should().Be(JsonValueKind.Array);
        logs.EnumerateArray()
            .Select(x => x.GetProperty("action").GetString())
            .Should()
            .Contain("TENANT_USER_ADDED");
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static async Task<(string Token, string RefreshToken, Guid TenantId)> RegisterTenant(HttpClient client, string email, string password)
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
        var refreshToken = body.GetProperty("refreshToken").GetString()!;
        var tenantId = body.GetProperty("tenantId").GetGuid();

        return (token, refreshToken, tenantId);
    }
}
