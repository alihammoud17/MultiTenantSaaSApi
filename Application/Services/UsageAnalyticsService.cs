using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public sealed class UsageAnalyticsService : IUsageAnalyticsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public UsageAnalyticsService(ApplicationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<TenantUsageAnalyticsSummary> GetTenantUsageSummaryAsync(
        int days = 30,
        string? action = null,
        CancellationToken cancellationToken = default)
    {
        var boundedDays = Math.Clamp(days, 1, 90);
        var windowEndUtc = DateTime.UtcNow;
        var windowStartUtc = windowEndUtc.AddDays(-boundedDays);

        var query = _dbContext.AuditLogs
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantContext.TenantId)
            .Where(x => x.Timestamp >= windowStartUtc && x.Timestamp <= windowEndUtc);

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Action == action);
        }

        var events = await query
            .Select(x => new { x.UserId, x.Action, x.Timestamp })
            .ToListAsync(cancellationToken);

        var totalEvents = events.Count;
        var distinctUsers = events
            .Where(x => x.UserId != Guid.Empty)
            .Select(x => x.UserId)
            .Distinct()
            .Count();

        var dailyCounts = events
            .GroupBy(x => x.Timestamp.Date)
            .Select(g => new TenantUsageDailyPoint(
                DateTime.SpecifyKind(g.Key, DateTimeKind.Utc),
                g.Count()))
            .OrderBy(x => x.DayUtc)
            .ToList();

        var topActions = events
            .GroupBy(x => x.Action)
            .Select(g => new TenantUsageActionPoint(g.Key, g.Count()))
            .OrderByDescending(x => x.EventCount)
            .ThenBy(x => x.Action)
            .Take(10)
            .ToList();

        return new TenantUsageAnalyticsSummary(
            _tenantContext.TenantId,
            windowStartUtc,
            windowEndUtc,
            totalEvents,
            distinctUsers,
            dailyCounts,
            topActions);
    }
}
