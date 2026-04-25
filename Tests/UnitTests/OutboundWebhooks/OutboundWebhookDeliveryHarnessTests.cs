using System.Net;
using System.Text.Json;
using Domain.Entites;
using FluentAssertions;

namespace Tests.UnitTests.OutboundWebhooks;

public class OutboundWebhookDeliveryHarnessTests
{
    [Fact]
    public async Task Harness_ShouldSuppressDuplicatePublish_AndEventuallyDeliverAfterTransientFailure()
    {
        const string sourceEventKey = "source-replay-safe-evt-1";

        await using var harness = await OutboundWebhookDeliveryHarness.CreateAsync(
            OutboundWebhookDeliveryHarness.WebhookDispatchOutcome.Failure(HttpStatusCode.InternalServerError),
            OutboundWebhookDeliveryHarness.WebhookDispatchOutcome.Success(HttpStatusCode.OK));

        var seeded = await harness.SeedTenantEndpointAsync();
        var publishRequest = new OutboundWebhookPublishRequestBuilder()
            .ForTenant(seeded.TenantId)
            .WithSourceEventKey(sourceEventKey)
            .Build();

        await harness.PublishAsync(publishRequest);
        await harness.PublishAsync(publishRequest);

        (await harness.GetEventCountAsync(seeded.TenantId)).Should().Be(1, "source-event dedupe should suppress duplicate publish attempts");
        (await harness.GetDeliveryCountAsync(seeded.TenantId)).Should().Be(1, "duplicate source-event publish should not enqueue additional deliveries");

        var firstAttemptStartedAtUtc = DateTime.UtcNow;
        await harness.DispatchDueDeliveriesOnceAsync();
        var firstAttemptFinishedAtUtc = DateTime.UtcNow;

        var firstDeliveryState = await harness.GetSingleDeliveryBySourceEventKeyAsync(sourceEventKey);
        firstDeliveryState.Status.Should().Be(OutboundWebhookDeliveryStatus.RetryScheduled);
        firstDeliveryState.AttemptCount.Should().Be(1);
        firstDeliveryState.LastAttemptAtUtc.Should().NotBeNull();
        firstDeliveryState.LastResponseStatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        firstDeliveryState.LastError.Should().Be("HTTP 500");
        firstDeliveryState.DeliveredAtUtc.Should().BeNull();

        var expectedRetryDelay = TimeSpan.FromSeconds(10);
        firstDeliveryState.NextAttemptAtUtc.Should().BeOnOrAfter(firstAttemptStartedAtUtc + expectedRetryDelay - TimeSpan.FromSeconds(1));
        firstDeliveryState.NextAttemptAtUtc.Should().BeOnOrBefore(firstAttemptFinishedAtUtc + expectedRetryDelay + TimeSpan.FromSeconds(1));

        await harness.ForceDeliveryDueAsync(firstDeliveryState.Id, DateTime.UtcNow.AddSeconds(-1));
        await harness.DispatchDueDeliveriesOnceAsync();

        var finalDeliveryState = await harness.GetSingleDeliveryAsync(seeded.TenantId);
        finalDeliveryState.Status.Should().Be(OutboundWebhookDeliveryStatus.Succeeded);
        finalDeliveryState.AttemptCount.Should().Be(2);
        finalDeliveryState.LastAttemptAtUtc.Should().NotBeNull();
        finalDeliveryState.LastResponseStatusCode.Should().Be((int)HttpStatusCode.OK);
        finalDeliveryState.LastError.Should().BeNull();
        finalDeliveryState.DeliveredAtUtc.Should().NotBeNull();
        finalDeliveryState.NextAttemptAtUtc.Should().BeOnOrBefore(DateTime.UtcNow);

        harness.CapturedRequests.Should().HaveCount(2);
        harness.CapturedRequests[0].RequestUri.Should().Be(harness.CapturedRequests[1].RequestUri);
        harness.CapturedRequests[0].CapturedAtUtc.Should().BeBefore(harness.CapturedRequests[1].CapturedAtUtc);
        harness.CapturedRequests[0].Headers["X-Tenant-Webhook-Idempotency-Key"]
            .Should()
            .Be(harness.CapturedRequests[1].Headers["X-Tenant-Webhook-Idempotency-Key"]);
        harness.CapturedRequests[0].Headers["X-Tenant-Webhook-Delivery"]
            .Should()
            .Be(harness.CapturedRequests[1].Headers["X-Tenant-Webhook-Delivery"]);
        harness.CapturedRequests[0].Headers["X-Tenant-Webhook-Contract-Version"]
            .Should()
            .Be("2026-04-13");
        harness.CapturedRequests[0].Headers.Should().ContainKey("X-Tenant-Webhook-Timestamp");
        harness.CapturedRequests[0].Headers.Should().ContainKey("X-Tenant-Webhook-Signature");

        using var firstAttemptBody = JsonDocument.Parse(harness.CapturedRequests[0].Body);
        using var secondAttemptBody = JsonDocument.Parse(harness.CapturedRequests[1].Body);
        firstAttemptBody.RootElement.GetProperty("correlationId").GetString()
            .Should()
            .Be(secondAttemptBody.RootElement.GetProperty("correlationId").GetString());
        firstAttemptBody.RootElement.GetProperty("eventId").GetString()
            .Should()
            .Be(secondAttemptBody.RootElement.GetProperty("eventId").GetString());
        firstAttemptBody.RootElement.GetProperty("tenantId").GetGuid()
            .Should()
            .Be(seeded.TenantId);
    }

    [Fact]
    public async Task Harness_ShouldSupportDeterministicTerminalFailureInspection()
    {
        await using var harness = await OutboundWebhookDeliveryHarness.CreateAsync(
            Enumerable.Repeat(
                OutboundWebhookDeliveryHarness.WebhookDispatchOutcome.Failure(HttpStatusCode.BadGateway),
                count: 6).ToArray());

        var seeded = await harness.SeedTenantEndpointAsync();

        await harness.PublishAsync(new OutboundWebhookPublishRequestBuilder()
            .ForTenant(seeded.TenantId)
            .Build());

        for (var i = 0; i < 6; i++)
        {
            await harness.DispatchDueDeliveriesOnceAsync();

            var delivery = await harness.GetSingleDeliveryAsync(seeded.TenantId);
            if (delivery.Status == OutboundWebhookDeliveryStatus.Exhausted)
            {
                break;
            }

            await harness.ForceDeliveryDueAsync(delivery.Id, DateTime.UtcNow.AddSeconds(-1));
        }

        var exhaustedDelivery = await harness.GetSingleDeliveryAsync(seeded.TenantId);
        exhaustedDelivery.Status.Should().Be(OutboundWebhookDeliveryStatus.Exhausted);
        exhaustedDelivery.AttemptCount.Should().Be(6);
        exhaustedDelivery.LastAttemptAtUtc.Should().NotBeNull();
        exhaustedDelivery.LastResponseStatusCode.Should().Be((int)HttpStatusCode.BadGateway);
        exhaustedDelivery.LastError.Should().Be("HTTP 502");
        exhaustedDelivery.DeliveredAtUtc.Should().BeNull();
        exhaustedDelivery.NextAttemptAtUtc.Should().BeOnOrBefore(DateTime.UtcNow);

        harness.CapturedRequests.Should().HaveCount(6);
        harness.CapturedRequests.Select(x => x.Headers["X-Tenant-Webhook-Delivery"]).Distinct().Should().ContainSingle();
        harness.CapturedRequests.Select(x => x.Headers["X-Tenant-Webhook-Idempotency-Key"]).Distinct().Should().ContainSingle();
        harness.CapturedRequests.Select(x => x.Headers["X-Tenant-Webhook-Timestamp"]).Should().OnlyHaveUniqueItems();
    }
}
