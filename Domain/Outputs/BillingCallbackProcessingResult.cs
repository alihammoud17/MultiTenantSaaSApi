namespace Domain.Outputs;

public sealed record BillingCallbackProcessingResult(
    bool Success,
    bool IsDuplicate,
    string Message,
    Guid TenantId,
    Guid SubscriptionId,
    string EventId,
    string AppliedStatus,
    string AppliedPlanId);
