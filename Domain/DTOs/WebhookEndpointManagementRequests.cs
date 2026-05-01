namespace Domain.DTOs;

public sealed record CreateWebhookEndpointRequest(string Name, string CallbackUrl, string? SubscribedEventTypes);
public sealed record UpdateWebhookEndpointRequest(string Name, string CallbackUrl, string? SubscribedEventTypes);
public sealed record SetWebhookEndpointStatusRequest(bool IsActive);
