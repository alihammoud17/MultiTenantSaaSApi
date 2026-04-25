using System.Net;
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
        finalDeliveryState.LastResponseStatusCode.Should().Be((int)HttpStatusCode.OK);
        finalDeliveryState.LastError.Should().BeNull();
        finalDeliveryState.DeliveredAtUtc.Should().NotBeNull();

        harness.CapturedRequests.Should().HaveCount(2);
        harness.CapturedRequests[0].Headers["X-Tenant-Webhook-Idempotency-Key"]
            .Should()
            .Be(harness.CapturedRequests[1].Headers["X-Tenant-Webhook-Idempotency-Key"]);
        harness.CapturedRequests[0].Headers["X-Tenant-Webhook-Delivery"]
            .Should()
            .Be(harness.CapturedRequests[1].Headers["X-Tenant-Webhook-Delivery"]);
        harness.CapturedRequests[0].Headers.Should().ContainKey("X-Tenant-Webhook-Timestamp");
        harness.CapturedRequests[0].Headers.Should().ContainKey("X-Tenant-Webhook-Signature");
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
        exhaustedDelivery.LastResponseStatusCode.Should().Be((int)HttpStatusCode.BadGateway);
        exhaustedDelivery.LastError.Should().Be("HTTP 502");
        exhaustedDelivery.DeliveredAtUtc.Should().BeNull();
        harness.CapturedRequests.Should().HaveCount(6);
    }
}
