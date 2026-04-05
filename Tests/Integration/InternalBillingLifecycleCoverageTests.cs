using System.Net;
using FluentAssertions;
using Domain.Entites;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class InternalBillingLifecycleCoverageTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public InternalBillingLifecycleCoverageTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldApplyActivatedRenewedGraceStartedExpiredTransitions()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-lifecycle-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var subscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, auth.TenantId);

        var activatedAt = DateTime.UtcNow.AddDays(-1);
        var renewedAt = DateTime.UtcNow;
        var periodEndsAt = renewedAt.AddMonths(1);
        var graceEndsAt = renewedAt.AddDays(7);

        var activated = await SecurityTestHelpers.PostSignedBillingEventAsync(client, new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-activated-{Guid.NewGuid():N}",
            eventType = "subscription.activated",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = "plan-pro",
            occurredAtUtc = activatedAt,
            effectiveAtUtc = periodEndsAt,
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        var renewed = await SecurityTestHelpers.PostSignedBillingEventAsync(client, new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-renewed-{Guid.NewGuid():N}",
            eventType = "subscription.renewed",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = renewedAt,
            effectiveAtUtc = periodEndsAt,
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        var graceStarted = await SecurityTestHelpers.PostSignedBillingEventAsync(client, new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-grace-{Guid.NewGuid():N}",
            eventType = "subscription.grace_period_started",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = renewedAt,
            effectiveAtUtc = graceEndsAt,
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        var expired = await SecurityTestHelpers.PostSignedBillingEventAsync(client, new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-expired-{Guid.NewGuid():N}",
            eventType = "subscription.expired",
            provider = "stripe",
            providerEventId = $"stripe-{Guid.NewGuid():N}",
            tenantId = auth.TenantId,
            subscriptionId,
            targetPlanId = (string?)null,
            occurredAtUtc = graceEndsAt,
            effectiveAtUtc = graceEndsAt,
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        activated.StatusCode.Should().Be(HttpStatusCode.OK);
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        graceStarted.StatusCode.Should().Be(HttpStatusCode.OK);
        expired.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = await db.Subscriptions.SingleAsync(x => x.Id == subscriptionId);

        subscription.PlanId.Should().Be("plan-pro");
        subscription.Status.Should().Be(SubscriptionStatus.Expired);
        subscription.CurrentPeriodStart.Should().BeCloseTo(renewedAt, TimeSpan.FromSeconds(1));
        subscription.CurrentPeriodEnd.Should().BeCloseTo(periodEndsAt, TimeSpan.FromSeconds(1));
        subscription.GracePeriodEndsAtUtc.Should().BeCloseTo(graceEndsAt, TimeSpan.FromSeconds(1));
    }
}
