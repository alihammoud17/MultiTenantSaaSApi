namespace Domain.Interfaces;

public interface IWebhookEndpointManagementService
{
    Task<Guid> CreateEndpointAsync(
        Guid tenantId,
        string name,
        string callbackUrl,
        string subscribedEventTypes,
        CancellationToken cancellationToken = default);
}
