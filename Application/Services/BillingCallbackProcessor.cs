using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public sealed class BillingCallbackProcessor : IBillingCallbackProcessor
{
    private static readonly HashSet<string> SupportedContractVersions = ["2026-03-18"];
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<BillingCallbackProcessor> _logger;

    public BillingCallbackProcessor(ApplicationDbContext dbContext, ILogger<BillingCallbackProcessor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<BillingCallbackProcessingResult> ProcessAsync(BillingCallbackRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var existingEvent = await _dbContext.BillingEventInboxes
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.EventId == request.EventId, cancellationToken);

        if (existingEvent is not null)
        {
            _logger.LogInformation(
                "Billing callback ignored as duplicate. EventId: {EventId}, TenantId: {TenantId}, SubscriptionId: {SubscriptionId}",
                request.EventId,
                request.TenantId,
                request.SubscriptionId);

            var duplicateStatus = await _dbContext.Subscriptions
                .Where(x => x.Id == request.SubscriptionId && x.TenantId == request.TenantId)
                .Select(x => new { x.Status, x.PlanId })
                .SingleAsync(cancellationToken);

            return new BillingCallbackProcessingResult(
                true,
                true,
                "Billing event already processed.",
                request.TenantId,
                request.SubscriptionId,
                request.EventId,
                duplicateStatus.Status.ToString(),
                duplicateStatus.PlanId);
        }

        var subscription = await _dbContext.Subscriptions
            .Include(x => x.Tenant)
            .SingleOrDefaultAsync(x => x.Id == request.SubscriptionId && x.TenantId == request.TenantId, cancellationToken);

        if (subscription is null)
        {
            throw new InvalidOperationException("Subscription mapping could not be validated for the supplied tenant.");
        }

        if (subscription.Tenant.Status != TenantStatus.Active)
        {
            throw new InvalidOperationException("Billing callback cannot be applied to a non-active tenant.");
        }

        if (!string.IsNullOrWhiteSpace(request.TargetPlanId))
        {
            var planExists = await _dbContext.Plans
                .AnyAsync(x => x.Id == request.TargetPlanId && x.IsActive, cancellationToken);

            if (!planExists)
            {
                throw new InvalidOperationException("Billing callback referenced an unknown or inactive target plan.");
            }
        }

        ApplyEvent(subscription, request);

        var now = DateTime.UtcNow;
        var inboxEntry = new BillingEventInbox
        {
            Id = Guid.NewGuid(),
            EventId = request.EventId,
            ContractVersion = request.ContractVersion,
            EventType = request.EventType,
            Provider = request.Provider,
            ProviderEventId = request.ProviderEventId,
            CorrelationId = request.CorrelationId,
            TenantId = request.TenantId,
            SubscriptionId = request.SubscriptionId,
            TargetPlanId = request.TargetPlanId,
            OccurredAtUtc = request.OccurredAtUtc,
            EffectiveAtUtc = request.EffectiveAtUtc,
            ReceivedAtUtc = now,
            ProcessedAtUtc = now
        };

        _dbContext.BillingEventInboxes.Add(inboxEntry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Billing callback applied. EventId: {EventId}, EventType: {EventType}, TenantId: {TenantId}, SubscriptionId: {SubscriptionId}, Status: {Status}, PlanId: {PlanId}, CorrelationId: {CorrelationId}",
            request.EventId,
            request.EventType,
            request.TenantId,
            request.SubscriptionId,
            subscription.Status,
            subscription.PlanId,
            request.CorrelationId);

        return new BillingCallbackProcessingResult(
            true,
            false,
            "Billing event processed.",
            request.TenantId,
            request.SubscriptionId,
            request.EventId,
            subscription.Status.ToString(),
            subscription.PlanId);
    }

    private static void ValidateRequest(BillingCallbackRequest request)
    {
        if (!SupportedContractVersions.Contains(request.ContractVersion))
        {
            throw new InvalidOperationException("Unsupported billing contract version.");
        }

        if (string.IsNullOrWhiteSpace(request.EventId) ||
            string.IsNullOrWhiteSpace(request.EventType) ||
            string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.ProviderEventId) ||
            string.IsNullOrWhiteSpace(request.CorrelationId) ||
            request.TenantId == Guid.Empty ||
            request.SubscriptionId == Guid.Empty)
        {
            throw new InvalidOperationException("Billing callback is missing required fields.");
        }
    }

    private static void ApplyEvent(Subscription subscription, BillingCallbackRequest request)
    {
        var effectiveAtUtc = request.EffectiveAtUtc ?? request.OccurredAtUtc;

        switch (request.EventType)
        {
            case "subscription.activated":
            case "subscription.renewed":
                if (!string.IsNullOrWhiteSpace(request.TargetPlanId))
                {
                    subscription.PlanId = request.TargetPlanId;
                }

                subscription.Status = SubscriptionStatus.Active;
                subscription.CurrentPeriodStart = request.OccurredAtUtc;
                subscription.CurrentPeriodEnd = effectiveAtUtc;
                subscription.ScheduledPlanId = null;
                subscription.ScheduledPlanEffectiveAtUtc = null;
                subscription.GracePeriodEndsAtUtc = null;
                subscription.CanceledAtUtc = null;
                break;

            case "subscription.plan_changed":
                if (string.IsNullOrWhiteSpace(request.TargetPlanId))
                {
                    throw new InvalidOperationException("Plan change events require a target plan.");
                }

                subscription.PlanId = request.TargetPlanId;
                subscription.Status = SubscriptionStatus.Active;
                subscription.ScheduledPlanId = null;
                subscription.ScheduledPlanEffectiveAtUtc = null;
                break;

            case "subscription.downgrade_scheduled":
                if (string.IsNullOrWhiteSpace(request.TargetPlanId))
                {
                    throw new InvalidOperationException("Downgrade scheduling events require a target plan.");
                }

                subscription.ScheduledPlanId = request.TargetPlanId;
                subscription.ScheduledPlanEffectiveAtUtc = request.EffectiveAtUtc ?? subscription.CurrentPeriodEnd;
                break;

            case "subscription.canceled":
                subscription.Status = SubscriptionStatus.Canceled;
                subscription.CanceledAtUtc = effectiveAtUtc;
                subscription.ScheduledPlanId = null;
                subscription.ScheduledPlanEffectiveAtUtc = null;
                subscription.GracePeriodEndsAtUtc = null;
                break;

            case "subscription.grace_period_started":
            case "invoice.payment_failed":
                subscription.Status = SubscriptionStatus.GracePeriod;
                subscription.GracePeriodEndsAtUtc = request.EffectiveAtUtc ?? request.OccurredAtUtc.AddDays(7);
                break;

            case "subscription.grace_period_expired":
            case "subscription.expired":
                if (!string.IsNullOrWhiteSpace(subscription.ScheduledPlanId) &&
                    subscription.ScheduledPlanEffectiveAtUtc.HasValue &&
                    subscription.ScheduledPlanEffectiveAtUtc.Value <= effectiveAtUtc)
                {
                    subscription.PlanId = subscription.ScheduledPlanId;
                }

                subscription.Status = SubscriptionStatus.Expired;
                subscription.GracePeriodEndsAtUtc = effectiveAtUtc;
                subscription.ScheduledPlanId = null;
                subscription.ScheduledPlanEffectiveAtUtc = null;
                break;

            default:
                throw new InvalidOperationException("Unsupported billing event type.");
        }
    }
}
