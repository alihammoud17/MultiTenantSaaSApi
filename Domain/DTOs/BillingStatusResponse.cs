using System;
using System.Collections.Generic;

namespace Domain.DTOs;

public sealed record BillingStatusResponse(
    string PlanId,
    string PlanName,
    string SubscriptionStatus,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? ScheduledPlanEffectiveAtUtc,
    string? ScheduledPlanId,
    DateTime? GracePeriodEndsAtUtc,
    DateTime? CanceledAtUtc,
    IReadOnlyCollection<string> AvailableActions);
