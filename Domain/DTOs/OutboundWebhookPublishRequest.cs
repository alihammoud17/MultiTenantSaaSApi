namespace Domain.DTOs;

public sealed record OutboundWebhookPublishRequest(
    Guid TenantId,
    string EventType,
    object Data,
    string CorrelationId,
    DateTime? OccurredAtUtc = null,
    string? SourceEventKey = null);
