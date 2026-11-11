using Domain.DTOs;

namespace Tests.UnitTests.OutboundWebhooks;

internal sealed class OutboundWebhookPublishRequestBuilder
{
    private Guid _tenantId = Guid.NewGuid();
    private string _eventType = "tenant.subscription.updated";
    private object _payload = new { subscriptionId = "sub-1", status = "Active" };
    private string _correlationId = "corr-outbound-webhook-harness";
    private DateTime? _occurredAtUtc = new DateTime(2026, 04, 25, 0, 0, 0, DateTimeKind.Utc);
    private string? _sourceEventKey = $"source-{Guid.NewGuid():N}";

    public OutboundWebhookPublishRequestBuilder ForTenant(Guid tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    public OutboundWebhookPublishRequestBuilder WithSourceEventKey(string? sourceEventKey)
    {
        _sourceEventKey = sourceEventKey;
        return this;
    }

    public OutboundWebhookPublishRequest Build()
    {
        return new OutboundWebhookPublishRequest(
            _tenantId,
            _eventType,
            _payload,
            _correlationId,
            _occurredAtUtc,
            _sourceEventKey);
    }
}
