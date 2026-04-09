namespace Domain.Outputs;

public sealed record EntitlementEnforcementResult(bool Allowed, string? DenialReason, EntitlementEvaluationResult Evaluation);
