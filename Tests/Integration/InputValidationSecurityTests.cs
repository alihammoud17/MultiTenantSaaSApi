using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Tests.Integration;

public class InputValidationSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public InputValidationSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MissingRequiredFields_ShouldReturnBadRequest_ForRefreshAndUserInvite()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"validate-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { tenantId = auth.TenantId, refreshToken = "" });
        refresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var invite = await client.PostAsJsonAsync("/api/admin/tenant/users", new { email = "", password = "Passw0rd!" });
        invite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MalformedGuidRoute_ShouldNotMatchAndReturn404()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"guid-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PutAsJsonAsync("/api/admin/tenant/users/not-a-guid/role", new { role = "ADMIN" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BadPaginationAndDateFilters_ShouldFailSafely()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"page-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var invalidDate = await client.GetAsync("/api/tenant/audit-logs?fromUtc=not-a-date");
        invalidDate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var clampedPaging = await client.GetAsync("/api/tenant/audit-logs?page=-5&pageSize=0");
        clampedPaging.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnexpectedProperties_AreIgnoredWithoutPrivilegeEscalation()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"overpost-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync("/api/admin/tenant/users", new
        {
            email = $"regular-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER",
            tenantId = Guid.NewGuid(),
            isSystemAdmin = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("MEMBER");
    }

    [Fact]
    public async Task MalformedBillingHeadersAndPayload_ShouldBeRejected()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var payload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.plan_changed",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = Guid.NewGuid(),
            subscriptionId = Guid.NewGuid(),
            targetPlanId = "plan-pro",
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = (DateTime?)null,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };

        var missingHeaders = await client.PostAsJsonAsync("/api/internal/billing/subscription-events", payload);
        missingHeaders.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var invalidJson = await SecurityTestHelpers.PostSignedBillingEventAsync(
            client,
            payload,
            overrideJson: "{\"broken\":true",
            includeTimestamp: true,
            includeSignature: true);

        invalidJson.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
