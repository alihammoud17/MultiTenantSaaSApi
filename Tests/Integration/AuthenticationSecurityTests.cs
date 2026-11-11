using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

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

        var response = await client.GetAsync("/api/v1/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MalformedBearerToken_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bad.token.value");

        var response = await client.GetAsync("/api/v1/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidToken_WithTenantHeaderOverride_ShouldReturnForbiddenForTenantMismatch()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"auth-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"auth-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantB.TenantId.ToString());

        var response = await client.GetAsync("/api/v1/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("TenantMismatch");
    }

    [Fact]
    public async Task StaleRotatedRefreshToken_ShouldFail()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"stale-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var rotate = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);

        var staleUse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });

        staleUse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutThenRefreshSameToken_ShouldFail()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"logout-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidRevokePayload_ShouldReturnValidationFailure()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"revoke-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/v1/auth/revoke", new { tenantId = Guid.Empty, refreshToken = "" });

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
        var inviteResponse = await client.PostAsJsonAsync("/api/v1/auth/invites", new
        {
            email = invitedEmail,
            role = "MEMBER"
        });
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var invitePayload = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inviteToken = invitePayload.GetProperty("inviteToken").GetString();

        var acceptResponse = await client.PostAsJsonAsync("/api/v1/auth/invites/accept", new
        {
            tenantId = admin.TenantId,
            inviteToken,
            password = "Passw0rd!"
        });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
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

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Passw0rd!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondRefreshToken = loginPayload.GetProperty("refreshToken").GetString();

        var inventoryResponse = await client.GetAsync("/api/v1/auth/sessions");
        inventoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await inventoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        sessions.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        var revokeResponse = await client.PostAsJsonAsync("/api/v1/auth/sessions/revoke-all", new
        {
            tenantId = auth.TenantId
        });
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokePayload = await revokeResponse.Content.ReadFromJsonAsync<JsonElement>();
        revokePayload.GetProperty("revokedCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var refreshOne = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId = auth.TenantId, refreshToken = auth.RefreshToken });
        var refreshTwo = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { tenantId = auth.TenantId, refreshToken = secondRefreshToken });

        refreshOne.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var refreshOnePayload = await refreshOne.Content.ReadFromJsonAsync<JsonElement>();
        refreshOnePayload.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");

        refreshTwo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var refreshTwoPayload = await refreshTwo.Content.ReadFromJsonAsync<JsonElement>();
        refreshTwoPayload.GetProperty("error").GetString().Should().Be("Invalid or expired refresh token");
    }

    [Fact]
    public async Task MfaEnrollment_ShouldSucceed_WithValidTotpCode()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"mfa-enroll-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var initiateResponse = await client.PostAsync("/api/v1/auth/mfa/enroll/initiate", null);
        initiateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initiateBody = await initiateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var enrollmentToken = initiateBody.GetProperty("enrollmentToken").GetString();
        var secret = initiateBody.GetProperty("secret").GetString();

        var completeResponse = await client.PostAsJsonAsync("/api/v1/auth/mfa/enroll/verify", new
        {
            enrollmentToken,
            code = GenerateTotpCode(secret!)
        });

        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SensitiveAction_ShouldRequireStepUp_WhenMfaIsEnabled()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"mfa-stepup-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var initiateResponse = await client.PostAsync("/api/v1/auth/mfa/enroll/initiate", null);
        var initiateBody = await initiateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var enrollmentToken = initiateBody.GetProperty("enrollmentToken").GetString();
        var secret = initiateBody.GetProperty("secret").GetString();

        var completeResponse = await client.PostAsJsonAsync("/api/v1/auth/mfa/enroll/verify", new
        {
            enrollmentToken,
            code = GenerateTotpCode(secret!)
        });
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var missingStepUp = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"blocked-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        missingStepUp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var stepUpResponse = await client.PostAsJsonAsync("/api/v1/auth/mfa/step-up", new
        {
            code = GenerateTotpCode(secret!),
            purpose = "admin_sensitive"
        });
        stepUpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var stepUpBody = await stepUpResponse.Content.ReadFromJsonAsync<JsonElement>();
        var stepUpToken = stepUpBody.GetProperty("stepUpToken").GetString();

        client.DefaultRequestHeaders.Remove("X-Step-Up-Token");
        client.DefaultRequestHeaders.Add("X-Step-Up-Token", stepUpToken);

        var withStepUp = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"allowed-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        withStepUp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task SensitiveAction_ShouldRejectStepUpToken_WithWrongPurpose()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"mfa-purpose-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var initiateResponse = await client.PostAsync("/api/v1/auth/mfa/enroll/initiate", null);
        var initiateBody = await initiateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var enrollmentToken = initiateBody.GetProperty("enrollmentToken").GetString();
        var secret = initiateBody.GetProperty("secret").GetString();

        var completeResponse = await client.PostAsJsonAsync("/api/v1/auth/mfa/enroll/verify", new
        {
            enrollmentToken,
            code = GenerateTotpCode(secret!)
        });
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var stepUpResponse = await client.PostAsJsonAsync("/api/v1/auth/mfa/step-up", new
        {
            code = GenerateTotpCode(secret!),
            purpose = "billing_sensitive"
        });
        stepUpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var stepUpToken = (await stepUpResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stepUpToken").GetString();

        client.DefaultRequestHeaders.Remove("X-Step-Up-Token");
        client.DefaultRequestHeaders.Add("X-Step-Up-Token", stepUpToken);

        var forbiddenResponse = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"purpose-blocked-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });

        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var payload = await forbiddenResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetString().Should().Be("Invalid or expired step-up token");
    }

    [Fact]
    public async Task CompleteVerification_ShouldRejectReplayedToken()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var email = $"verify-replay-{Guid.NewGuid():N}@example.com";
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, email, "Passw0rd!");
        var userId = ResolveUserId(auth.TenantId, email);

        var verificationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verificationToken)));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserVerificationTokens.Add(new UserVerificationToken
            {
                Id = Guid.NewGuid(),
                TenantId = auth.TenantId,
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        var firstUse = await client.PostAsJsonAsync("/api/v1/auth/verification/complete", new
        {
            tenantId = auth.TenantId,
            verificationToken
        });
        firstUse.StatusCode.Should().Be(HttpStatusCode.OK);

        var replayUse = await client.PostAsJsonAsync("/api/v1/auth/verification/complete", new
        {
            tenantId = auth.TenantId,
            verificationToken
        });
        replayUse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await replayUse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Invalid or expired verification token");
    }

    [Fact]
    public async Task CompletePasswordReset_ShouldRejectReplayedToken_AndRotateCredentials()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var email = $"reset-replay-{Guid.NewGuid():N}@example.com";
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, email, "Passw0rd!");
        var userId = ResolveUserId(auth.TenantId, email);

        var resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resetToken)));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                TenantId = auth.TenantId,
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        var firstUse = await client.PostAsJsonAsync("/api/v1/auth/password-reset/complete", new
        {
            tenantId = auth.TenantId,
            resetToken,
            newPassword = "N3wPassw0rd!"
        });
        firstUse.StatusCode.Should().Be(HttpStatusCode.OK);

        var replayUse = await client.PostAsJsonAsync("/api/v1/auth/password-reset/complete", new
        {
            tenantId = auth.TenantId,
            resetToken,
            newPassword = "An0therPass!"
        });
        replayUse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var oldPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Passw0rd!"
        });
        oldPasswordLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "N3wPassw0rd!"
        });
        newPasswordLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Guid ResolveUserId(Guid tenantId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.Users.Single(u => u.TenantId == tenantId && u.Email == email).Id;
    }

    private static string GenerateTotpCode(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sanitized = secret.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();
        var bitBuffer = 0;
        var bitsInBuffer = 0;
        foreach (var character in sanitized)
        {
            var value = alphabet.IndexOf(character);
            if (value < 0)
                throw new InvalidOperationException("Invalid base32 secret");
            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }

        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> timestep = stackalloc byte[8];
        BitConverter.TryWriteBytes(timestep, step);
        if (BitConverter.IsLittleEndian)
            timestep.Reverse();
        using var hmac = new HMACSHA1(bytes.ToArray());
        var hash = hmac.ComputeHash(timestep.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
                         | ((hash[offset + 1] & 0xff) << 16)
                         | ((hash[offset + 2] & 0xff) << 8)
                         | (hash[offset + 3] & 0xff);
        return (binaryCode % 1_000_000).ToString("D6");
    }
}
