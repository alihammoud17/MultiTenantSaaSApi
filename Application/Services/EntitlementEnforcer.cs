using Domain.Interfaces;
using Domain.Outputs;

namespace Application.Services;

public class EntitlementEnforcer : IEntitlementEnforcer
{
    private readonly IEntitlementEvaluator _evaluator;

    public EntitlementEnforcer(IEntitlementEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public async Task<EntitlementEnforcementResult> EnsureFeatureEnabledAsync(Guid tenantId, string entitlementKey, CancellationToken cancellationToken = default)
    {
        var evaluation = await _evaluator.EvaluateAsync(tenantId, entitlementKey, cancellationToken);
        if (evaluation.IsAllowed)
        {
            return new EntitlementEnforcementResult(true, null, evaluation);
        }

        return new EntitlementEnforcementResult(false, $"Entitlement '{entitlementKey}' is not enabled for this tenant.", evaluation);
    }
}
