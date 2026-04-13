namespace Domain.Entites;

public sealed class OutboundWebhookEvent
{
    public Guid Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string ContractVersion { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string? SourceEventKey { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
