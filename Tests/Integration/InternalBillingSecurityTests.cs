using System.Net;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class InternalBillingSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public InternalBillingSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MissingTimestampOrSignature_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var payload = BuildPayload();

        var missingTimestamp = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload, includeTimestamp: false);
        var missingSignature = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload, includeSignature: false);

        missingTimestamp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        missingSignature.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidSignature_AndTamperedBody_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var payload = BuildPayload();

        var invalidSignature = await SecurityTestHelpers.PostSignedBillingEventAsync(
            client,
            payload,
            forcedSignature: "sha256=deadbeef");

        var originalJson = JsonSerializer.Serialize(payload);
        var tamperedJson = originalJson.Replace("plan-pro", "plan-free");
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signatureForOriginal = SecurityTestHelpers.ComputeSignatureForTests(timestamp, originalJson);
        var tampered = await SecurityTestHelpers.PostSignedBillingEventAsync(
            client,
            payload,
            timestamp: DateTimeOffset.Parse(timestamp),
            overrideJson: tamperedJson,
            forcedSignature: signatureForOriginal);

        invalidSignature.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        tampered.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredTimestamp_ShouldReturn401()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var payload = BuildPayload();

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(
            client,
            payload,
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-10));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FakeSubscriptionOrWrongTenantMapping_ShouldReturn400()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-sec-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-sec-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantASubscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, tenantA.TenantId);

        var fakeSubscription = BuildPayload(tenantA.TenantId, Guid.NewGuid());
        var fakeResponse = await SecurityTestHelpers.PostSignedBillingEventAsync(client, fakeSubscription);

        var wrongTenant = BuildPayload(tenantB.TenantId, tenantASubscriptionId);
        var wrongTenantResponse = await SecurityTestHelpers.PostSignedBillingEventAsync(client, wrongTenant);

        fakeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wrongTenantResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DuplicateEventId_ShouldBeIdempotent()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-sec-dup-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);
        var eventId = $"evt-{Guid.NewGuid():N}";

        var payload = BuildPayload(auth.TenantId, subscriptionId, eventId);

        var first = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);
        var second = await SecurityTestHelpers.PostSignedBillingEventAsync(client, payload);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.BillingEventInboxes.Count(x => x.EventId == eventId).Should().Be(1);
    }

    private static object BuildPayload(Guid? tenantId = null, Guid? subscriptionId = null, string? eventId = null)
    {
        return new
        {
            contractVersion = "2026-03-18",
            eventId = eventId ?? $"evt-{Guid.NewGuid():N}",
            eventType = "subscription.plan_changed",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = tenantId ?? Guid.NewGuid(),
            subscriptionId = subscriptionId ?? Guid.NewGuid(),
            targetPlanId = "plan-pro",
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = (DateTime?)null,
            correlationId = $"corr-{Guid.NewGuid():N}"
        };
    }
}
