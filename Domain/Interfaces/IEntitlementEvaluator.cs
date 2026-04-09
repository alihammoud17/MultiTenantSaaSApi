using Domain.Outputs;

namespace Domain.Interfaces;

public interface IEntitlementEvaluator
{
    Task<EntitlementEvaluationResult> EvaluateAsync(Guid tenantId, string entitlementKey, CancellationToken cancellationToken = default);
}
