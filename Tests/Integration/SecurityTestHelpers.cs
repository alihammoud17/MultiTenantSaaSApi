using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Tests.Integration;

internal static class SecurityTestHelpers
{
    internal static HttpClient CreateHttpsClient(ApiWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    internal static async Task<(string Token, string RefreshToken, Guid TenantId)> RegisterTenantAsync(HttpClient client, string email, string password)
    {
        var (token, refreshToken, tenantId, _) = await RegisterVerifiedTenantAsync(client, email, password, $"tenant-{Guid.NewGuid():N}");
        return (token, refreshToken, tenantId);
    }

    /// <summary>
    /// Registers a tenant, verifies the admin email with the captured token, and logs in.
    /// Requires a factory that registers <see cref="CapturingIdentityNotificationService"/>.
    /// </summary>
    internal static async Task<(string Token, string RefreshToken, Guid TenantId, string Subdomain)> RegisterVerifiedTenantAsync(
        HttpClient client,
        string email,
        string password,
        string subdomain)
    {
        var tenantId = await RegisterPendingTenantAsync(client, email, password, subdomain);
        await CompleteRegistrationVerificationAsync(client, tenantId);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("token").GetString()!,
            body.GetProperty("refreshToken").GetString()!,
            body.GetProperty("tenantId").GetGuid(),
            subdomain);
    }

    internal static async Task<Guid> RegisterPendingTenantAsync(HttpClient client, string email, string password, string subdomain)
    {
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = $"Company {Guid.NewGuid():N}",
            subdomain,
            adminEmail = email,
            adminPassword = password
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("tenantId").GetGuid();
    }

    internal static async Task CompleteRegistrationVerificationAsync(HttpClient client, Guid tenantId)
    {
        var verify = await client.PostAsJsonAsync("/api/v1/auth/verification/complete", new
        {
            tenantId,
            verificationToken = CapturingIdentityNotificationService.GetLatestVerificationToken(tenantId)
        });

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    internal static async Task<string> CreateMemberAndLoginAsync(HttpClient client)
    {
        var memberEmail = $"member-{Guid.NewGuid():N}@example.com";

        var addResponse = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = memberEmail,
            password = "Passw0rd!",
            role = "MEMBER"
        });

        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = memberEmail,
            password = "Passw0rd!"
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    internal static async Task<HttpResponseMessage> PostSignedBillingEventAsync(
        HttpClient client,
        object payload,
        DateTimeOffset? timestamp = null,
        string? overrideJson = null,
        bool includeTimestamp = true,
        bool includeSignature = true,
        string? forcedSignature = null)
    {
        var json = overrideJson ?? JsonSerializer.Serialize(payload);
        var resolvedTimestamp = (timestamp ?? DateTimeOffset.UtcNow).ToString("O");
        var signature = forcedSignature ?? ComputeSignatureForTests(resolvedTimestamp, json);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/billing/subscription-events");

        if (includeTimestamp)
        {
            request.Headers.Add("X-Billing-Timestamp", resolvedTimestamp);
        }

        if (includeSignature)
        {
            request.Headers.Add("X-Billing-Signature", signature);
        }

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    /// <summary>
    /// A signed operator request to <c>/api/internal/tenants/{tenantId}/{action}</c>, ready to send (or to replay).
    /// </summary>
    internal static HttpRequestMessage CreateSignedTenantOperation(
        Guid tenantId,
        string action,
        object? body,
        DateTimeOffset? timestamp = null,
        string secret = ApiWebApplicationFactory.TenantOperationsSecret)
    {
        var json = body is null ? string.Empty : JsonSerializer.Serialize(body);
        var resolvedTimestamp = (timestamp ?? DateTimeOffset.UtcNow).ToString("O");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/tenants/{tenantId}/{action}");
        request.Headers.Add("X-Operations-Timestamp", resolvedTimestamp);
        request.Headers.Add("X-Operations-Signature", Application.Services.HmacRequestSignature.Compute(secret, resolvedTimestamp, json));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    internal static async Task<HttpResponseMessage> SuspendTenantAsync(HttpClient client, Guid tenantId, string reason = "test suspension")
    {
        using var request = CreateSignedTenantOperation(tenantId, "suspend", new { reason, actor = "integration-test" });
        return await client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> ReactivateTenantAsync(HttpClient client, Guid tenantId)
    {
        using var request = CreateSignedTenantOperation(tenantId, "reactivate", new { reason = "test reactivation", actor = "integration-test" });
        return await client.SendAsync(request);
    }

    internal static Guid GetSubscriptionId(ApiWebApplicationFactory factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.Subscriptions.Single(x => x.TenantId == tenantId).Id;
    }

    /// <summary>
    /// Mints an access token with the same claims and HS256 signing as <c>JwtService</c>, using the factory's JWT settings
    /// unless overridden. Pass <paramref name="tenantId"/> null to leave out the <c>tenant_id</c> claim. An expiry in the
    /// past gives an expired token; <c>nbf</c> is then set before it, since <see cref="JwtSecurityToken"/> requires nbf &lt; exp.
    /// A replacement <paramref name="signingKey"/> must be at least 32 bytes, or HS256 signing throws.
    /// </summary>
    internal static string CreateTestJwt(
        Guid userId,
        Guid? tenantId,
        DateTime expiresUtc,
        string role = "ADMIN",
        string signingKey = ApiWebApplicationFactory.JwtSecret,
        string issuer = ApiWebApplicationFactory.JwtIssuer,
        string audience = ApiWebApplicationFactory.JwtAudience)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, $"{userId:N}@example.com"),
            new("role", role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (tenantId is not null)
        {
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        }

        var now = DateTime.UtcNow;
        var notBefore = expiresUtc > now ? now : expiresUtc.AddMinutes(-5);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(issuer, audience, claims, notBefore, expiresUtc, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Exchanges an API access token for an MCP token at <c>POST /api/v1/auth/mcp-token</c>.</summary>
    internal static async Task<string> GetMcpTokenAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/mcp-token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the MCP token exchange should succeed");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    /// <summary>The user id (<c>sub</c> claim) of an access token returned by the API.</summary>
    internal static Guid GetUserId(string token) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject);

    internal static string ComputeSignatureForTests(string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("billing-integration-test-secret"));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}\n{payload}"));
        return "sha256=" + Convert.ToHexString(signatureBytes).ToLowerInvariant();
    }
}
