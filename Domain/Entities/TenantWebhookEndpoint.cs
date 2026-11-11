namespace Domain.Entities;

public sealed class TenantWebhookEndpoint
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string SigningSecret { get; set; } = string.Empty;
    public DateTime SigningSecretIssuedAtUtc { get; set; }
    public string? NextSigningSecret { get; set; }
    public DateTime? NextSigningSecretIssuedAtUtc { get; set; }
    public string SubscribedEventTypes { get; set; } = "*";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
