using Domain.Outputs;

namespace Domain.Interfaces;

public interface IUsageAnalyticsService
{
    Task<TenantUsageAnalyticsSummary> GetTenantUsageSummaryAsync(
        int days = 30,
        string? action = null,
        CancellationToken cancellationToken = default);
}
