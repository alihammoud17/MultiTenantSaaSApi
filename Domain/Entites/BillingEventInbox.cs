namespace Domain.Entites;

public class BillingEventInbox
{
    public Guid Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string ContractVersion { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderEventId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string? TargetPlanId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? EffectiveAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
}
