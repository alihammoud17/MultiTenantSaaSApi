using Domain.DTOs;

namespace Domain.Interfaces;

public interface IOutboundWebhookPublisher
{
    Task PublishAsync(OutboundWebhookPublishRequest request, CancellationToken cancellationToken = default);
}
