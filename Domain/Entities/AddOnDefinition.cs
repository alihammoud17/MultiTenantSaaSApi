using System;
using System.Collections.Generic;

namespace Domain.Entities;

public class AddOnDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? BillingProviderProductRef { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<AddOnEntitlement> Entitlements { get; set; } = new List<AddOnEntitlement>();
}
