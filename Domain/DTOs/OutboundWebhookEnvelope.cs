namespace Domain.DTOs;

public sealed record OutboundWebhookEnvelope(
    string ContractVersion,
    string EventId,
    Guid TenantId,
    string EventType,
    string CorrelationId,
    DateTime OccurredAtUtc,
    object Data);
