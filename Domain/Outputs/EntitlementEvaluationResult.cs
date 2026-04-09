using System;

namespace Domain.Outputs;

public sealed record EntitlementEvaluationResult(
    Guid TenantId,
    string EntitlementKey,
    string? Value,
    bool IsAllowed,
    string ResolvedFrom,
    string SubscriptionStatus,
    DateTime EvaluatedAtUtc,
    string? CorrelationId);
