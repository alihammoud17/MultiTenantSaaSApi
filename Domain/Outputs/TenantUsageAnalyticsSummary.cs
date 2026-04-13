namespace Domain.Outputs;

public sealed record TenantUsageAnalyticsSummary(
    Guid TenantId,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    int TotalEvents,
    int DistinctUsers,
    IReadOnlyCollection<TenantUsageDailyPoint> DailyEvents,
    IReadOnlyCollection<TenantUsageActionPoint> TopActions);

public sealed record TenantUsageDailyPoint(
    DateTime DayUtc,
    int EventCount);

public sealed record TenantUsageActionPoint(
    string Action,
    int EventCount);
