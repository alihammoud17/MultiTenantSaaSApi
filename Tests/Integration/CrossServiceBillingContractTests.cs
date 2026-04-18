using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class CrossServiceBillingContractTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CrossServiceBillingContractTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidSignedCallbackPayload_ShouldBeAccepted()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"contract-valid-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);
        var payload = BuildValidPayload(auth.TenantId, subscriptionId);

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isDuplicate").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("eventType")]
    [InlineData("provider")]
    [InlineData("providerEventId")]
    [InlineData("correlationId")]
    public async Task MissingRequiredStringFields_ShouldReturn400(string missingField)
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"contract-missing-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);

        var payload = new Dictionary<string, object?>
        {
            ["contractVersion"] = "2026-03-18",
            ["eventId"] = $"evt-{Guid.NewGuid():N}",
            ["eventType"] = "subscription.canceled",
            ["provider"] = "stripe",
            ["providerEventId"] = $"stripe-{Guid.NewGuid():N}",
            ["tenantId"] = auth.TenantId,
            ["subscriptionId"] = subscriptionId,
            ["targetPlanId"] = null,
            ["occurredAtUtc"] = DateTime.UtcNow,
            ["effectiveAtUtc"] = null,
            ["correlationId"] = $"corr-{Guid.NewGuid():N}"
        };

        payload.Remove(missingField);

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Billing callback is missing required fields.");
    }

    [Fact]
    public async Task InvalidContractVersion_ShouldReturn400()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"contract-version-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);
        var payload = BuildValidPayload(auth.TenantId, subscriptionId, new Dictionary<string, object?>
        {
            ["contractVersion"] = "2024-01-01"
        });

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Unsupported billing contract version.");
    }

    [Fact]
    public async Task InvalidSignature_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"contract-sig-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);
        var payload = BuildValidPayload(auth.TenantId, subscriptionId);

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload, forcedSignature: "sha256=invalid");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MismatchedTenantAndSubscriptionIdentifiers_ShouldReturn400()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"contract-mismatch-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"contract-mismatch-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantASubscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, tenantA.TenantId);
        var payload = BuildValidPayload(tenantB.TenantId, tenantASubscriptionId, new Dictionary<string, object?>
        {
            ["eventType"] = "subscription.canceled",
            ["targetPlanId"] = null
        });

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Subscription mapping could not be validated for the supplied tenant.");
    }

    [Fact]
    public async Task DuplicateEventId_ShouldReturnDuplicateAndPersistSingleInboxRecord()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"contract-duplicate-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);
        var eventId = $"evt-{Guid.NewGuid():N}";
        var payload = BuildValidPayload(auth.TenantId, subscriptionId, new Dictionary<string, object?>
        {
            ["eventId"] = eventId
        });

        var first = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);
        var second = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.BillingEventInboxes.Count(x => x.EventId == eventId).Should().Be(1);
    }

    private static Dictionary<string, object?> BuildValidPayload(Guid tenantId, Guid subscriptionId, Dictionary<string, object?>? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["contractVersion"] = "2026-03-18",
            ["eventId"] = $"evt-{Guid.NewGuid():N}",
            ["eventType"] = "subscription.plan_changed",
            ["provider"] = "stripe",
            ["providerEventId"] = $"stripe-{Guid.NewGuid():N}",
            ["tenantId"] = tenantId,
            ["subscriptionId"] = subscriptionId,
            ["targetPlanId"] = "plan-pro",
            ["occurredAtUtc"] = DateTime.UtcNow,
            ["effectiveAtUtc"] = null,
            ["correlationId"] = $"corr-{Guid.NewGuid():N}"
        };

        if (overrides is null)
        {
            return payload;
        }

        foreach (var entry in overrides)
        {
            payload[entry.Key] = entry.Value;
        }

        return payload;
    }
}
