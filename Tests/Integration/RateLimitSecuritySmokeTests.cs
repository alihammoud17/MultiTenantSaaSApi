using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace Tests.Integration;

public class RateLimitSecuritySmokeTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RateLimitSecuritySmokeTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtectedRequest_ShouldEmitRateLimitHeaders_InTestHarness()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"rl-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.GetAsync("/api/admin/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("X-RateLimit-Limit").Should().BeTrue();
        response.Headers.Contains("X-RateLimit-Remaining").Should().BeTrue();
        response.Headers.Contains("X-RateLimit-Reset").Should().BeTrue();
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldBypassTenantRateLimitContext()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"rl-billing-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);

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
            effectiveAtUtc = (DateTime?)null,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("X-RateLimit-Limit").Should().BeFalse();
        response.Headers.Contains("X-RateLimit-Remaining").Should().BeFalse();
        response.Headers.Contains("X-RateLimit-Reset").Should().BeFalse();
    }
}
