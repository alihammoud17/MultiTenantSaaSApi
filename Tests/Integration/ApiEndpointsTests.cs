using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Domain.Authorization;
using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
    public async Task Health_ShouldReturnCorrelationId_AndDetailedPayload()
    {
        using var client = CreateClient();
        const string correlationId = "corr-health-test";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.GetAsync("/health");

        response.Headers.GetValues("X-Correlation-ID").Single().Should().Be(correlationId);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("service").GetString().Should().Be("multi-tenant-saas-api");
        body.GetProperty("correlationId").GetString().Should().Be(correlationId);
        body.GetProperty("checks").TryGetProperty("self", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Metrics_ShouldReturnSnapshot()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("metrics").GetProperty("service").GetString().Should().Be("multi-tenant-saas-api");
        body.GetProperty("metrics").GetProperty("requestsByRoute").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Health_ShouldReturnStableShape_WithoutSensitiveFields()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("status", out _).Should().BeTrue();
        body.TryGetProperty("service", out _).Should().BeTrue();
        body.TryGetProperty("correlationId", out _).Should().BeTrue();
        body.TryGetProperty("totalDuration", out _).Should().BeTrue();
        body.TryGetProperty("checks", out _).Should().BeTrue();

        var serialized = body.GetRawText().ToLowerInvariant();
        serialized.Should().NotContain("connectionstring");
        serialized.Should().NotContain("password");
        serialized.Should().NotContain("secret");
        serialized.Should().NotContain("token");
    }

    [Fact]
    public async Task Metrics_ShouldReturnStableShape_WithoutSensitiveFields()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("correlationId", out _).Should().BeTrue();
        body.TryGetProperty("generatedAtUtc", out _).Should().BeTrue();
        body.TryGetProperty("metrics", out var metrics).Should().BeTrue();
        metrics.TryGetProperty("service", out _).Should().BeTrue();
        metrics.TryGetProperty("activeRequests", out _).Should().BeTrue();
        metrics.TryGetProperty("requestsByRoute", out _).Should().BeTrue();
        metrics.TryGetProperty("requestsByStatus", out _).Should().BeTrue();

        var serialized = body.GetRawText().ToLowerInvariant();
        serialized.Should().NotContain("connectionstring");
        serialized.Should().NotContain("password");
        serialized.Should().NotContain("secret");
        serialized.Should().NotContain("token");
    }

    [Fact]
    public async Task Register_ShouldCreatePendingTenant_AndReturnAcceptedWithoutTokens()
    {
        using var client = CreateClient();
        var subdomain = $"acme-{Guid.NewGuid():N}";
        var payload = new
        {
            companyName = $"Acme {Guid.NewGuid():N}",
            subdomain,
            adminEmail = $"admin-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        };

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("pending_verification");
        body.GetProperty("verificationExpiresAt").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddHours(47));
        body.TryGetProperty("token", out _).Should().BeFalse();
        body.TryGetProperty("refreshToken", out _).Should().BeFalse();
        var tenantId = body.GetProperty("tenantId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await dbContext.Tenants.SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(TenantStatus.PendingVerification);
        var admin = await dbContext.Users.SingleAsync(u => u.TenantId == tenantId);
        admin.EmailVerifiedAt.Should().BeNull();
        (await dbContext.UserVerificationTokens.CountAsync(t => t.TenantId == tenantId && t.UserId == admin.Id)).Should().Be(1);
        (await dbContext.RefreshTokens.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId)).Should().BeFalse();
        (await dbContext.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId && a.Action == "TENANT_REGISTERED")).Should().BeTrue();
    }

    [Fact]
    public async Task Login_ShouldBeRejected_UntilRegistrationIsVerified_ThenActivateTenant()
    {
        using var client = CreateClient();
        var email = $"pending-{Guid.NewGuid():N}@example.com";
        var tenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(client, email, "Passw0rd!", $"pending-{Guid.NewGuid():N}");

        var beforeVerification = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Passw0rd!" });

        beforeVerification.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await beforeVerification.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("error").GetString().Should().Be("Email is not verified");

        await SecurityTestHelpers.CompleteRegistrationVerificationAsync(client, tenantId);

        var afterVerification = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Passw0rd!" });

        afterVerification.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await afterVerification.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        body.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.Tenants.SingleAsync(t => t.Id == tenantId)).Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task PendingTenant_ShouldRejectTenantScopedRequests_EvenWithAValidJwt()
    {
        using var client = CreateClient();
        var email = $"pending-jwt-{Guid.NewGuid():N}@example.com";
        var tenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(client, email, "Passw0rd!", $"pending-jwt-{Guid.NewGuid():N}");

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await dbContext.Users.Include(u => u.Tenant).SingleAsync(u => u.TenantId == tenantId);
            token = scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateToken(user, user.Tenant);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/tenant");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Register_ShouldReclaimEmailAndSubdomain_FromExpiredPendingRegistration()
    {
        using var client = CreateClient();
        var email = $"squatted-{Guid.NewGuid():N}@example.com";
        var subdomain = $"squatted-{Guid.NewGuid():N}";
        var squatterTenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(client, email, "Squatt3r!", subdomain);
        await BackdateTenantCreationAsync(squatterTenantId, TimeSpan.FromHours(49));

        var ownerTenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(client, email, "Passw0rd!", subdomain);

        ownerTenantId.Should().NotBe(squatterTenantId);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.Tenants.AnyAsync(t => t.Id == squatterTenantId)).Should().BeFalse();
        (await dbContext.Users.AnyAsync(u => u.TenantId == squatterTenantId)).Should().BeFalse();
        (await dbContext.UserVerificationTokens.AnyAsync(t => t.TenantId == squatterTenantId)).Should().BeFalse();
        (await dbContext.Subscriptions.AnyAsync(x => x.TenantId == squatterTenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task Register_ShouldReclaimEmail_WhenExpiredPendingRegistrationUsedADifferentSubdomain()
    {
        using var client = CreateClient();
        var email = $"squatted-email-{Guid.NewGuid():N}@example.com";
        var squatterTenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(client, email, "Squatt3r!", $"squat-{Guid.NewGuid():N}");
        await BackdateTenantCreationAsync(squatterTenantId, TimeSpan.FromHours(49));

        var ownerTenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(client, email.ToUpperInvariant(), "Passw0rd!", $"owner-{Guid.NewGuid():N}");

        await SecurityTestHelpers.CompleteRegistrationVerificationAsync(client, ownerTenantId);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = email.ToUpperInvariant(), password = "Passw0rd!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_ShouldStillBlockEmailAndSubdomain_WhilePendingRegistrationIsWithinItsWindow()
    {
        using var client = CreateClient();
        var email = $"recent-{Guid.NewGuid():N}@example.com";
        var subdomain = $"recent-{Guid.NewGuid():N}";
        var pendingTenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(client, email, "Passw0rd!", subdomain);
        await BackdateTenantCreationAsync(pendingTenantId, TimeSpan.FromHours(47));

        var sameEmail = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Second Company",
            subdomain = $"other-{Guid.NewGuid():N}",
            adminEmail = email,
            adminPassword = "Passw0rd!"
        });
        var sameSubdomain = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Third Company",
            subdomain,
            adminEmail = $"other-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        // The email answer is indistinguishable from a new registration, but the pending tenant keeps the email.
        sameEmail.StatusCode.Should().Be(HttpStatusCode.Accepted);
        sameSubdomain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sameSubdomain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("Subdomain already taken");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.Tenants.SingleAsync(t => t.Id == pendingTenantId)).Status.Should().Be(TenantStatus.PendingVerification);
        (await dbContext.Users.Where(u => u.Email == email).Select(u => u.TenantId).ToListAsync())
            .Should().Equal(pendingTenantId);
    }

    [Fact]
    public async Task Register_ShouldNeverReclaim_AVerifiedTenant()
    {
        using var client = CreateClient();
        var email = $"verified-{Guid.NewGuid():N}@example.com";
        var subdomain = $"verified-{Guid.NewGuid():N}";
        var (_, _, tenantId, _) = await SecurityTestHelpers.RegisterVerifiedTenantAsync(client, email, "Passw0rd!", subdomain);
        await BackdateTenantCreationAsync(tenantId, TimeSpan.FromDays(30));

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Late Company",
            subdomain,
            adminEmail = email,
            adminPassword = "Passw0rd!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.Tenants.SingleAsync(t => t.Id == tenantId)).Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task Login_ShouldReturnJwt_ForExistingUser()
    {
        using var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var password = "Passw0rd!";

        await RegisterTenant(client, email, password);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
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

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
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

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
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

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
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

        var revokeResponse = await client.PostAsJsonAsync("/api/v1/auth/revoke", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken,
            reason = "TEST_REVOKE"
        });

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
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

        var firstRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstRefresh.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedRefreshToken = firstBody.GetProperty("refreshToken").GetString();
        rotatedRefreshToken.Should().NotBeNullOrWhiteSpace();
        rotatedRefreshToken.Should().NotBe(auth.RefreshToken);

        var oldTokenRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        oldTokenRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var rotatedTokenRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
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

        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new
        {
            tenantId = auth.TenantId,
            refreshToken = auth.RefreshToken
        });

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
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

        var revokeResponse = await client.PostAsJsonAsync("/api/v1/auth/revoke", new
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

        var revokeResponse = await client.PostAsJsonAsync("/api/v1/auth/revoke", new
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

        var revokeResponse = await client.PostAsJsonAsync("/api/v1/auth/revoke", new
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

        var first = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "First Company",
            subdomain,
            adminEmail = $"first-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var second = await client.PostAsJsonAsync("/api/v1/auth/register", new
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
    public async Task Register_ShouldNormalizeSubdomain_AndTreatCaseVariantsAsDuplicates()
    {
        using var client = CreateClient();
        var subdomain = $"case-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "  Case Company  ",
            subdomain = $"  {subdomain.ToUpperInvariant()} ",
            adminEmail = $"case-first-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var tenantId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = await dbContext.Tenants.SingleAsync(t => t.Id == tenantId);
            tenant.Subdomain.Should().Be(subdomain);
            tenant.Name.Should().Be("Case Company");
        }

        var second = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Second Company",
            subdomain,
            adminEmail = $"case-second-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Subdomain already taken");
    }

    [Fact]
    public async Task Register_WithAlreadyRegisteredEmail_ShouldBeIndistinguishableFromNewRegistration_AndCreateNothing()
    {
        using var client = CreateClient();
        var email = $"dup-email-{Guid.NewGuid():N}@example.com";
        var secondSubdomain = $"email-b-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "First Company",
            subdomain = $"email-a-{Guid.NewGuid():N}",
            adminEmail = email,
            adminPassword = "Passw0rd!"
        });

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var noticesBefore = CapturingIdentityNotificationService.GetExistingAccountNoticeCount(email);

        var second = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Second Company",
            subdomain = secondSubdomain,
            adminEmail = $"  {email.ToUpperInvariant()} ",
            adminPassword = "Passw0rd!"
        });

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.EnumerateObject().Select(p => p.Name).Should().Equal(firstBody.EnumerateObject().Select(p => p.Name));
        secondBody.GetProperty("status").GetString().Should().Be(firstBody.GetProperty("status").GetString());
        var fakeTenantId = secondBody.GetProperty("tenantId").GetGuid();
        fakeTenantId.Should().NotBe(firstBody.GetProperty("tenantId").GetGuid());

        CapturingIdentityNotificationService.GetExistingAccountNoticeCount(email).Should().Be(noticesBefore + 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await dbContext.Tenants.AnyAsync(t => t.Id == fakeTenantId || t.Subdomain == secondSubdomain)).Should().BeFalse();
            (await dbContext.Users.CountAsync(u => u.Email.ToLower() == email)).Should().Be(1);
        }

        var verify = await client.PostAsJsonAsync("/api/v1/auth/verification/complete", new
        {
            tenantId = fakeTenantId,
            verificationToken = "not-a-real-token"
        });
        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("Invalid or expired verification token");
    }

    [Fact]
    public async Task Register_WithTakenSubdomainAndRegisteredEmail_ShouldReportSubdomainOnly()
    {
        using var client = CreateClient();
        var email = $"both-taken-{Guid.NewGuid():N}@example.com";
        var subdomain = $"both-taken-{Guid.NewGuid():N}";
        await SecurityTestHelpers.RegisterPendingTenantAsync(client, email, "Passw0rd!", subdomain);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Both Taken Co",
            subdomain,
            adminEmail = email,
            adminPassword = "Passw0rd!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("Subdomain already taken");
    }

    [Theory]
    [InlineData("www", "Subdomain is reserved")]
    [InlineData("acme.corp", "Subdomain may only contain lowercase letters, digits and hyphens, and cannot start or end with a hyphen")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Subdomain must be between 3 and 50 characters")]
    public async Task Register_ShouldReturnBadRequest_ForInvalidSubdomain(string subdomain, string expectedError)
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Invalid Subdomain Co",
            subdomain,
            adminEmail = $"invalid-sub-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be(expectedError);
    }

    [Fact]
    public async Task Register_ShouldReturnBadRequest_AndCreateNothing_WhenPasswordTooShort()
    {
        using var client = CreateClient();
        var subdomain = $"weak-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Weak Password Co",
            subdomain,
            adminEmail = $"weak-{Guid.NewGuid():N}@example.com",
            adminPassword = "a"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Admin password must be at least 8 characters");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.Tenants.AnyAsync(t => t.Subdomain == subdomain)).Should().BeFalse();
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_ForInvalidPassword()
    {
        using var client = CreateClient();
        var email = $"wrong-pass-{Guid.NewGuid():N}@example.com";

        await RegisterTenant(client, email, "Passw0rd!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "WrongPass123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid credentials");
    }

    [Fact]
    public async Task Login_UnknownEmail_ShouldMatchWrongPasswordResponse()
    {
        using var client = CreateClient();
        var email = $"known-{Guid.NewGuid():N}@example.com";
        await RegisterTenant(client, email, "Passw0rd!");

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "WrongPass123!" });
        var unknownEmail = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"unknown-{Guid.NewGuid():N}@example.com",
            password = "WrongPass123!"
        });

        unknownEmail.StatusCode.Should().Be(wrongPassword.StatusCode);
        (await unknownEmail.Content.ReadAsStringAsync()).Should().Be(await wrongPassword.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetPlans_ShouldReturnAvailablePlans_WithoutAuthOrTenantHeaders()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await response.Content.ReadFromJsonAsync<JsonElement>();

        plans.ValueKind.Should().Be(JsonValueKind.Array);
        plans.EnumerateArray().Select(x => x.GetProperty("id").GetString()).Should().Contain(["plan-free", "plan-pro"]);
    }

    [Fact]
    public async Task UpgradePlan_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-pro" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpgradePlan_ShouldSwitchTenantToProPlan_UsingJwtTenantClaim()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"up-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-pro" });

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

        var response = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "" });

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

        var response = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-does-not-exist" });

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

        var response = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-free" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Tenant is already on this plan");
    }

    [Fact]
    public async Task UpgradePlan_ShouldReturnForbidden_WhenEntitlementOverrideDisablesPlanUpgrade()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"upgrade-entitlement-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
            {
                Id = Guid.NewGuid(),
                TenantId = auth.TenantId,
                EntitlementKey = EntitlementKeys.BillingPlanUpgrade,
                Value = "false",
                Reason = "Feature temporarily disabled for this tenant",
                Source = TenantEntitlementOverrideSource.ManualCorrection,
                EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-1),
                CreatedUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var response = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-pro" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain(EntitlementKeys.BillingPlanUpgrade);
    }

    [Fact]
    public async Task AuditLogs_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/tenant/audit-logs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuditLogs_ShouldReturnTenantPlanChangedEvent()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var upgrade = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-pro" });
        upgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync("/api/v1/tenant/audit-logs");
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

        var upgrade = await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-pro" });
        upgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync("/api/v1/tenant/audit-logs?action=TENANT_PLAN_CHANGED");

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
    public async Task AdminTenantEndpoints_ShouldReturnTenantDetailsAndManageUsers()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"admin-mgmt-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var details = await client.GetAsync("/api/v1/admin/tenant");
        details.StatusCode.Should().Be(HttpStatusCode.OK);

        var added = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"new-user-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var addBody = await added.Content.ReadFromJsonAsync<JsonElement>();
        var newUserId = addBody.GetProperty("id").GetGuid();

        var roleChanged = await client.PutAsJsonAsync($"/api/v1/admin/tenant/users/{newUserId}/role", new
        {
            role = "ADMIN"
        });

        roleChanged.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await client.GetAsync("/api/v1/admin/tenant/users");
        users.StatusCode.Should().Be(HttpStatusCode.OK);
        var usersBody = await users.Content.ReadFromJsonAsync<JsonElement>();
        usersBody.EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).Should().Contain(newUserId);

        var remove = await client.DeleteAsync($"/api/v1/admin/tenant/users/{newUserId}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AdminTenantEndpoints_ShouldBeTenantScoped_WhenDeletingUser()
    {
        using var client = CreateClient();

        var tenantA = await RegisterTenant(client, $"tenant-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await RegisterTenant(client, $"tenant-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);

        var addUserInTenantA = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"tenant-a-user-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        addUserInTenantA.StatusCode.Should().Be(HttpStatusCode.Created);
        var addedBody = await addUserInTenantA.Content.ReadFromJsonAsync<JsonElement>();
        var tenantAUserId = addedBody.GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantB.Token);

        var crossTenantRemove = await client.DeleteAsync($"/api/v1/admin/tenant/users/{tenantAUserId}");
        crossTenantRemove.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminTenantEndpoints_ShouldEnforceRbac_ForMemberUser()
    {
        using var client = CreateClient();

        var adminAuth = await RegisterTenant(client, $"rbac-admin-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);

        var memberEmail = $"rbac-member-{Guid.NewGuid():N}@example.com";
        var addMember = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = memberEmail,
            password = "Passw0rd!",
            role = "MEMBER"
        });
        addMember.StatusCode.Should().Be(HttpStatusCode.Created);

        var memberLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = memberEmail,
            password = "Passw0rd!"
        });
        memberLogin.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await memberLogin.Content.ReadFromJsonAsync<JsonElement>();
        var memberToken = loginBody.GetProperty("token").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var forbidden = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"should-fail-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminTenantAuditLogs_ShouldReturnTenantScopedAuditData()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"admin-audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var addUser = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"audit-user-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        addUser.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.GetAsync("/api/v1/admin/tenant/audit-logs?action=TENANT_USER_ADDED");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await response.Content.ReadFromJsonAsync<JsonElement>();
        logs.ValueKind.Should().Be(JsonValueKind.Array);
        logs.EnumerateArray().Select(x => x.GetProperty("action").GetString()).Should().Contain("TENANT_USER_ADDED");
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldApplyPlanChange_WhenSignatureAndTenantMappingAreValid()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"billing-valid-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        Guid subscriptionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            subscriptionId = db.Subscriptions.Single(x => x.TenantId == auth.TenantId).Id;
        }

        var payload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.plan_changed",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = "plan-pro",
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = (DateTime?)null,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var response = await PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isDuplicate").GetBoolean().Should().BeFalse();
        body.GetProperty("appliedPlanId").GetString().Should().Be("plan-pro");
        body.GetProperty("appliedStatus").GetString().Should().Be("Active");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = verifyDb.Subscriptions.Single(x => x.Id == subscriptionId);
        subscription.TenantId.Should().Be(auth.TenantId);
        subscription.PlanId.Should().Be("plan-pro");
        verifyDb.BillingEventInboxes.Single(x => x.EventId == body.GetProperty("eventId").GetString());
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldPreserveRequestCorrelationHeader_AndPersistCallbackCorrelationId()
    {
        using var client = CreateClient();
        const string requestCorrelationId = "corr-internal-callback-quality-gate";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", requestCorrelationId);

        var auth = await RegisterTenant(client, $"billing-corr-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        Guid subscriptionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            subscriptionId = db.Subscriptions.Single(x => x.TenantId == auth.TenantId).Id;
        }

        var payloadCorrelationId = $"corr-payload-{Guid.NewGuid():N}";
        var payload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.renewed",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = DateTime.UtcNow.AddDays(30),
            correlationId = payloadCorrelationId
        };

        var response = await PostSignedBillingEventAsync(client, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Correlation-ID").Single().Should().Be(requestCorrelationId);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("eventId").GetString().Should().Be(payload.eventId);
        body.GetProperty("tenantId").GetGuid().Should().Be(auth.TenantId);
        body.GetProperty("subscriptionId").GetGuid().Should().Be(subscriptionId);
        body.GetProperty("appliedStatus").GetString().Should().Be("Active");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inbox = verifyDb.BillingEventInboxes.Single(x => x.EventId == payload.eventId);
        inbox.CorrelationId.Should().Be(payloadCorrelationId);
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldRejectCrossTenantSubscriptionMapping()
    {
        using var client = CreateClient();

        var tenantA = await RegisterTenant(client, $"billing-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await RegisterTenant(client, $"billing-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        Guid subscriptionIdForTenantA;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            subscriptionIdForTenantA = db.Subscriptions.Single(x => x.TenantId == tenantA.TenantId).Id;
        }

        var payload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.canceled",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = tenantB.TenantId,
            subscriptionId = subscriptionIdForTenantA,
            targetPlanId = (string?)null,
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = (DateTime?)null,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var response = await PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Subscription mapping could not be validated for the supplied tenant.");
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldScheduleDowngrade_AndPreserveCurrentPlanUntilEffectiveDate()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"billing-downgrade-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        Guid subscriptionId;
        DateTime currentPeriodEnd;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existingSubscription = db.Subscriptions.Single(x => x.TenantId == auth.TenantId);
            existingSubscription.PlanId = "plan-pro";
            db.SaveChanges();
            subscriptionId = existingSubscription.Id;
            currentPeriodEnd = existingSubscription.CurrentPeriodEnd;
        }

        var payload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.downgrade_scheduled",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = "plan-free",
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = currentPeriodEnd,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var response = await PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedSubscription = verifyDb.Subscriptions.Single(x => x.Id == subscriptionId);
        updatedSubscription.PlanId.Should().NotBe("plan-free");
        updatedSubscription.ScheduledPlanId.Should().Be("plan-free");
        updatedSubscription.ScheduledPlanEffectiveAtUtc.Should().BeCloseTo(currentPeriodEnd, TimeSpan.FromSeconds(1));
        updatedSubscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldEnterGracePeriod_ForFailedPaymentAndExpireWhenGraceEnds()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"billing-grace-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        Guid subscriptionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            subscriptionId = db.Subscriptions.Single(x => x.TenantId == auth.TenantId).Id;
        }

        var graceEndsAt = DateTime.UtcNow.AddDays(5);
        var failedPaymentPayload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "invoice.payment_failed",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = graceEndsAt,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var failedPaymentResponse = await PostSignedBillingEventAsync(client, failedPaymentPayload);
        failedPaymentResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var expiredPayload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.grace_period_expired",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = graceEndsAt,
            effectiveAtUtc = graceEndsAt,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var expiredResponse = await PostSignedBillingEventAsync(client, expiredPayload);
        expiredResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = verifyDb.Subscriptions.Single(x => x.Id == subscriptionId);
        subscription.Status.Should().Be(SubscriptionStatus.Expired);
        subscription.GracePeriodEndsAtUtc.Should().BeCloseTo(graceEndsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldCancelSubscription_AndClearPendingLifecycleState()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"billing-cancel-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        Guid subscriptionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existingSubscription = db.Subscriptions.Single(x => x.TenantId == auth.TenantId);
            existingSubscription.ScheduledPlanId = "plan-free";
            existingSubscription.ScheduledPlanEffectiveAtUtc = existingSubscription.CurrentPeriodEnd;
            existingSubscription.GracePeriodEndsAtUtc = DateTime.UtcNow.AddDays(3);
            db.SaveChanges();
            subscriptionId = existingSubscription.Id;
        }

        var canceledAt = DateTime.UtcNow;
        var payload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.canceled",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = canceledAt,
            effectiveAtUtc = canceledAt,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var response = await PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedSubscription = verifyDb.Subscriptions.Single(x => x.Id == subscriptionId);
        updatedSubscription.Status.Should().Be(SubscriptionStatus.Canceled);
        updatedSubscription.CanceledAtUtc.Should().BeCloseTo(canceledAt, TimeSpan.FromSeconds(1));
        updatedSubscription.ScheduledPlanId.Should().BeNull();
        updatedSubscription.ScheduledPlanEffectiveAtUtc.Should().BeNull();
        updatedSubscription.GracePeriodEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldBeIdempotent_ForDuplicateEvents()
    {
        using var client = CreateClient();

        var auth = await RegisterTenant(client, $"billing-duplicate-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        Guid subscriptionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            subscriptionId = db.Subscriptions.Single(x => x.TenantId == auth.TenantId).Id;
        }

        var payload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.canceled",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = (DateTime?)null,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var firstResponse = await PostSignedBillingEventAsync(client, payload);
        var secondResponse = await PostSignedBillingEventAsync(client, payload);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        verifyDb.BillingEventInboxes.Count(x => x.EventId == payload.eventId).Should().Be(1);
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static async Task<HttpResponseMessage> PostSignedBillingEventAsync(HttpClient client, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("billing-integration-test-secret"));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}\n{json}"));
        var signature = "sha256=" + Convert.ToHexString(signatureBytes).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/billing/subscription-events");
        request.Headers.Add("X-Billing-Timestamp", timestamp);
        request.Headers.Add("X-Billing-Signature", signature);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        return await client.SendAsync(request);
    }

    private async Task BackdateTenantCreationAsync(Guid tenantId, TimeSpan age)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await dbContext.Tenants.SingleAsync(t => t.Id == tenantId);
        tenant.CreatedAt = DateTime.UtcNow - age;
        await dbContext.SaveChangesAsync();
    }

    private static Task<(string Token, string RefreshToken, Guid TenantId)> RegisterTenant(HttpClient client, string email, string password)
    {
        return SecurityTestHelpers.RegisterTenantAsync(client, email, password);
    }
}
