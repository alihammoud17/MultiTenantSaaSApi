using System;
using System.Collections.Generic;

namespace Domain.Entities;

public class EntitlementDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public EntitlementValueType ValueType { get; set; } = EntitlementValueType.Boolean;
    public EntitlementCategory Category { get; set; } = EntitlementCategory.Feature;
    public bool IsActive { get; set; } = true;
    public string? DefaultValue { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PlanEntitlement> PlanEntitlements { get; set; } = new List<PlanEntitlement>();
}

public enum EntitlementValueType
{
    Boolean,
    Integer,
    Decimal,
    String,
    Json
}

public enum EntitlementCategory
{
    Feature,
    Quota,
    LimitBehavior,
    Operational
}
