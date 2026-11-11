namespace Domain.DTOs;

public sealed record WebhookEndpointSecretResponse(string SigningSecret, bool HasPendingSecretRotation);
