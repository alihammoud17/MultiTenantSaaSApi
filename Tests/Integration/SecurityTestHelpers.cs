using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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

    internal static string ComputeSignatureForTests(string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("billing-integration-test-secret"));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}\n{payload}"));
        return "sha256=" + Convert.ToHexString(signatureBytes).ToLowerInvariant();
    }
}
