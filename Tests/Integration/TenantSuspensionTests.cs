using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class TenantSuspensionTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Password = "Passw0rd!";
    private readonly ApiWebApplicationFactory _factory;

    public TenantSuspensionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SuspendRequest_WithMissingOrInvalidSignature_ShouldReturn401_AndLeaveTenantActive()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await RegisterAsync(client);

        using var unsigned = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/tenants/{tenant.TenantId}/suspend")
        {
            Content = JsonContent.Create(new { reason = "no signature" })
        };
        using var wrongSecret = SecurityTestHelpers.CreateSignedTenantOperation(tenant.TenantId, "suspend", new { reason = "wrong secret" }, secret: "not-the-secret");
        using var billingSecret = SecurityTestHelpers.CreateSignedTenantOperation(tenant.TenantId, "suspend", new { reason = "billing secret" }, secret: "billing-integration-test-secret");
        using var stale = SecurityTestHelpers.CreateSignedTenantOperation(tenant.TenantId, "suspend", new { reason = "stale" }, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10));

        foreach (var request in new[] { unsigned, wrongSecret, billingSecret, stale })
        {
            var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await GetTenantAsync(tenant.TenantId)).Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task SuspendRequest_WhenOperationsSecretIsUnset_ShouldAlwaysReturn401()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["TenantOperations:SharedSecret"] = "" })));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var tenant = await RegisterAsync(client);

        var response = await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetTenantAsync(tenant.TenantId)).Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task Suspend_ShouldSuspendTenant_RevokeRefreshTokens_AndAudit()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await RegisterAsync(client);

        var response = await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId, "chargeback investigation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Suspended");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Tenants.SingleAsync(t => t.Id == tenant.TenantId);
        stored.Status.Should().Be(TenantStatus.Suspended);
        stored.SuspensionReason.Should().Be("chargeback investigation");
        stored.StatusChangedAtUtc.Should().NotBeNull();

        var tokens = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.TenantId == tenant.TenantId).ToListAsync();
        tokens.Should().NotBeEmpty();
        tokens.Should().OnlyContain(t => t.RevokedAt != null && t.RevocationReason == "TENANT_SUSPENDED");

        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.TenantId == tenant.TenantId && a.Action == "TENANT_SUSPENDED");
        audit.Changes.Should().Contain("chargeback investigation").And.Contain("integration-test");
    }

    [Fact]
    public async Task Suspend_WithoutReason_ShouldReturn400()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await RegisterAsync(client);

        using var request = SecurityTestHelpers.CreateSignedTenantOperation(tenant.TenantId, "suspend", new { actor = "ops" });
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetTenantAsync(tenant.TenantId)).Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task SuspendedTenant_ExistingAccessToken_ShouldBeRejectedOnAuthenticatedAuthRoutes()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenant.Token);

        // Control: the token works on an authenticated /auth route before the suspension.
        (await client.GetAsync("/api/v1/auth/sessions")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var responses = new[]
        {
            await client.GetAsync("/api/v1/auth/sessions"),
            await client.PostAsJsonAsync("/api/v1/auth/invites", new { email = $"invitee-{Guid.NewGuid():N}@example.com", role = "MEMBER" }),
            await client.PostAsync("/api/v1/auth/mfa/enroll/initiate", null),
            await client.PostAsJsonAsync("/api/v1/auth/sessions/revoke-all", new { }),
            await client.GetAsync("/api/v1/admin/tenant")
        };

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("TenantSuspended");
        }

        client.DefaultRequestHeaders.Authorization = null;
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = tenant.Email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId = tenant.TenantId, refreshToken = tenant.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuspendedTenant_AnonymousIdentityFlows_ShouldBeRejected()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await RegisterAsync(client);
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@example.com";

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenant.Token);
        var invite = await client.PostAsJsonAsync("/api/v1/auth/invites", new { email = inviteeEmail, role = "MEMBER" });
        invite.StatusCode.Should().Be(HttpStatusCode.OK);
        var inviteToken = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inviteToken").GetString();
        client.DefaultRequestHeaders.Authorization = null;

        (await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var accept = await client.PostAsJsonAsync("/api/v1/auth/invites/accept", new
        {
            tenantId = tenant.TenantId,
            inviteToken,
            password = Password
        });
        accept.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var resetRequest = await client.PostAsJsonAsync("/api/v1/auth/password-reset/request", new
        {
            tenantId = tenant.TenantId,
            email = tenant.Email
        });
        resetRequest.StatusCode.Should().Be(HttpStatusCode.OK);

        var adminUserId = SeedResetAndVerificationTokens(tenant.TenantId, tenant.Email, out var resetToken, out var verificationToken);

        var resetComplete = await client.PostAsJsonAsync("/api/v1/auth/password-reset/complete", new
        {
            tenantId = tenant.TenantId,
            resetToken,
            newPassword = "N3wPassw0rd!"
        });
        resetComplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var verify = await client.PostAsJsonAsync("/api/v1/auth/verification/complete", new
        {
            tenantId = tenant.TenantId,
            verificationToken
        });
        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.AnyAsync(u => u.Email == inviteeEmail)).Should().BeFalse();
        // Only the token seeded above exists: the reset request issued none.
        (await db.PasswordResetTokens.CountAsync(t => t.UserId == adminUserId)).Should().Be(1);
        (await db.Tenants.SingleAsync(t => t.Id == tenant.TenantId)).Status.Should().Be(TenantStatus.Suspended);
        BCrypt.Net.BCrypt.Verify(Password, (await db.Users.SingleAsync(u => u.Id == adminUserId)).PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_ShouldRestoreLogin_ButKeepPreSuspensionRefreshTokensRevoked()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await RegisterAsync(client);
        (await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SecurityTestHelpers.ReactivateTenantAsync(client, tenant.TenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await GetTenantAsync(tenant.TenantId);
        stored.Status.Should().Be(TenantStatus.Active);
        stored.SuspensionReason.Should().BeNull();

        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = tenant.Email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId = tenant.TenantId, refreshToken = tenant.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenant.TenantId && a.Action == "TENANT_REACTIVATED"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task StatusOperations_ShouldRejectInvalidTransitions_AndUnknownTenants()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var active = await RegisterAsync(client);
        var pendingTenantId = await SecurityTestHelpers.RegisterPendingTenantAsync(
            client, $"pending-{Guid.NewGuid():N}@example.com", Password, $"pending-{Guid.NewGuid():N}");

        (await SecurityTestHelpers.ReactivateTenantAsync(client, active.TenantId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SecurityTestHelpers.SuspendTenantAsync(client, pendingTenantId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SecurityTestHelpers.SuspendTenantAsync(client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await SecurityTestHelpers.SuspendTenantAsync(client, active.TenantId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SecurityTestHelpers.SuspendTenantAsync(client, active.TenantId)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await GetTenantAsync(pendingTenantId)).Status.Should().Be(TenantStatus.PendingVerification);
        (await GetTenantAsync(active.TenantId)).Status.Should().Be(TenantStatus.Suspended);
    }

    [Fact]
    public async Task ReplayedReactivateRequest_ShouldNotUndoALaterSuspension()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenant = await RegisterAsync(client);
        (await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var reactivateTimestamp = DateTimeOffset.UtcNow;
        using (var reactivate = SecurityTestHelpers.CreateSignedTenantOperation(tenant.TenantId, "reactivate", new { actor = "ops" }, reactivateTimestamp))
        {
            (await client.SendAsync(reactivate)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await SecurityTestHelpers.SuspendTenantAsync(client, tenant.TenantId, "suspended again")).StatusCode.Should().Be(HttpStatusCode.OK);

        // An attacker re-sends the captured reactivate request (same timestamp and signature) within the skew window.
        using var replay = SecurityTestHelpers.CreateSignedTenantOperation(tenant.TenantId, "reactivate", new { actor = "ops" }, reactivateTimestamp);
        var replayResponse = await client.SendAsync(replay);

        replayResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var stored = await GetTenantAsync(tenant.TenantId);
        stored.Status.Should().Be(TenantStatus.Suspended);
        stored.SuspensionReason.Should().Be("suspended again");
    }

    private async Task<(string Token, string RefreshToken, Guid TenantId, string Email)> RegisterAsync(HttpClient client)
    {
        var email = $"suspension-{Guid.NewGuid():N}@example.com";
        var (token, refreshToken, tenantId) = await SecurityTestHelpers.RegisterTenantAsync(client, email, Password);
        return (token, refreshToken, tenantId, email);
    }

    private async Task<Tenant> GetTenantAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);
    }

    // Seeds a valid password-reset token and verification token for the tenant admin, as if issued before suspension.
    private Guid SeedResetAndVerificationTokens(Guid tenantId, string email, out string resetToken, out string verificationToken)
    {
        resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        verificationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = db.Users.Single(u => u.TenantId == tenantId && u.Email == email);

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            TokenHash = Hash(resetToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        db.UserVerificationTokens.Add(new UserVerificationToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            TokenHash = Hash(verificationToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        db.SaveChanges();
        return user.Id;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
