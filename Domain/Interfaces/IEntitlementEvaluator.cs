using Domain.Outputs;

namespace Domain.Interfaces;

public interface IEntitlementEvaluator
{
    Task<EntitlementEvaluationResult> EvaluateAsync(Guid tenantId, string entitlementKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates several keys against one subscription snapshot, with the same query count as a single key.
    /// Duplicate keys are evaluated once; the result has one entry per distinct key.
    /// </summary>
    /// <remarks>
    /// Within an HTTP request each tenant and key is resolved once and later calls reuse that result, so a change to
    /// a tenant's plan, add-ons or overrides made later in the same request is not seen by later evaluations.
    /// </remarks>
    Task<IReadOnlyDictionary<string, EntitlementEvaluationResult>> EvaluateManyAsync(
        Guid tenantId,
        IEnumerable<string> entitlementKeys,
        CancellationToken cancellationToken = default);
}
