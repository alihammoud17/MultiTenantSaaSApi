namespace Domain.Entities;

public sealed class OutboundWebhookDelivery
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid EndpointId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public int? LastResponseStatusCode { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public OutboundWebhookDeliveryStatus Status { get; set; } = OutboundWebhookDeliveryStatus.Pending;
}

public enum OutboundWebhookDeliveryStatus
{
    Pending = 0,
    Succeeded = 1,
    RetryScheduled = 2,
    Exhausted = 3
}
