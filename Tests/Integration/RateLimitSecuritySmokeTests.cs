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
}
