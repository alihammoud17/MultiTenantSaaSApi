using Domain.Outputs;

namespace Domain.Interfaces;

public interface IEntitlementEnforcer
{
    Task<EntitlementEnforcementResult> EnsureFeatureEnabledAsync(Guid tenantId, string entitlementKey, CancellationToken cancellationToken = default);
}
