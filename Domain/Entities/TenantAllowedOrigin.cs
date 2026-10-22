namespace Domain.Entities;

/// <summary>
/// A browser origin (<c>https://host[:port]</c>) a tenant has registered for cross-origin access to the API.
/// <see cref="Origin"/> is stored in its canonical lowercase form, exactly as browsers send it in the Origin header.
/// </summary>
public sealed class TenantAllowedOrigin
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
