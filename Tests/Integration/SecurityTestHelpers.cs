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
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            companyName = $"Company {Guid.NewGuid():N}",
            subdomain = $"tenant-{Guid.NewGuid():N}",
            adminEmail = email,
            adminPassword = password
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("token").GetString()!,
            body.GetProperty("refreshToken").GetString()!,
            body.GetProperty("tenantId").GetGuid());
    }

    internal static async Task<string> CreateMemberAndLoginAsync(HttpClient client)
    {
        var memberEmail = $"member-{Guid.NewGuid():N}@example.com";

        var addResponse = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = memberEmail,
            password = "Passw0rd!",
            role = "MEMBER"
        });

        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/auth/login", new
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
