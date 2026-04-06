namespace Domain.DTOs;

public sealed record CancelSubscriptionRequest(string? Reason);

public sealed record ReactivateSubscriptionRequest(string? Reason);
