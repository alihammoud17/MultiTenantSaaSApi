using System;

namespace Domain.Entites;

public class TenantEntitlementOverride
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EntitlementKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public TenantEntitlementOverrideSource Source { get; set; } = TenantEntitlementOverrideSource.ManualCorrection;
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public EntitlementDefinition EntitlementDefinition { get; set; } = null!;
}

public enum TenantEntitlementOverrideSource
{
    SupportGrant,
    Compensation,
    ManualCorrection
}
