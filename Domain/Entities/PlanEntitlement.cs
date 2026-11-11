using System;

namespace Domain.Entities;

public class PlanEntitlement
{
    public string PlanId { get; set; } = string.Empty;
    public string EntitlementKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public EntitlementSourceType Source { get; set; } = EntitlementSourceType.PlanDefault;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public Plan Plan { get; set; } = null!;
    public EntitlementDefinition EntitlementDefinition { get; set; } = null!;
}

public enum EntitlementSourceType
{
    PlanDefault,
    AddOn,
    Override,
    Default
}
