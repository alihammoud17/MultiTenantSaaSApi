using System;

namespace Domain.Entites;

public class TenantAddOnAssignment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string AddOnId { get; set; } = string.Empty;
    public TenantAddOnAssignmentStatus Status { get; set; } = TenantAddOnAssignmentStatus.Active;
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string? ExternalReference { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public AddOnDefinition AddOn { get; set; } = null!;
}

public enum TenantAddOnAssignmentStatus
{
    Active,
    Scheduled,
    Expired,
    Canceled
}
