namespace Domain.DTOs;

public sealed record BillingCallbackRequest(
    string ContractVersion,
    string EventId,
    string EventType,
    string Provider,
    string ProviderEventId,
    Guid TenantId,
    Guid SubscriptionId,
    string? TargetPlanId,
    DateTime OccurredAtUtc,
    DateTime? EffectiveAtUtc,
    string CorrelationId);
