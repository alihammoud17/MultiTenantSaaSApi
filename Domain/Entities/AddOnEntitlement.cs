using System;

namespace Domain.Entities;

public class AddOnEntitlement
{
    public string AddOnId { get; set; } = string.Empty;
    public string EntitlementKey { get; set; } = string.Empty;
    public AddOnEntitlementValueMode ValueMode { get; set; } = AddOnEntitlementValueMode.Set;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public AddOnDefinition AddOn { get; set; } = null!;
    public EntitlementDefinition EntitlementDefinition { get; set; } = null!;
}

public enum AddOnEntitlementValueMode
{
    Set,
    Increment,
    Max,
    Min
}
