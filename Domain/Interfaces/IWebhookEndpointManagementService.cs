namespace Domain.Interfaces;

public interface IWebhookEndpointManagementService
{
    Task<WebhookEndpointManagementResult> CreateEndpointAsync(
        Guid tenantId,
        string name,
        string callbackUrl,
        string subscribedEventTypes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WebhookEndpointManagementView>> ListEndpointsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<WebhookEndpointManagementView?> GetEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default);

    Task<WebhookEndpointManagementView?> UpdateEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        string name,
        string callbackUrl,
        string subscribedEventTypes,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteEndpointAsync(Guid tenantId, Guid endpointId, CancellationToken cancellationToken = default);

    Task<WebhookEndpointManagementView?> SetEndpointActiveStateAsync(
        Guid tenantId,
        Guid endpointId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<WebhookEndpointManagementResult?> RotateSigningSecretAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default);
}

public sealed record WebhookEndpointManagementView(
    Guid Id,
    string Name,
    string CallbackUrl,
    string SubscribedEventTypes,
    bool IsActive,
    DateTime SigningSecretIssuedAtUtc,
    DateTime? NextSigningSecretIssuedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record WebhookEndpointManagementResult(
    WebhookEndpointManagementView Endpoint,
    string SigningSecret,
    bool HasPendingSecretRotation);
