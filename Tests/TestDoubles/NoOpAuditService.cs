using Domain.Interfaces;
using Domain.Outputs;

namespace Tests.TestDoubles;

// For services built by hand in tests that don't assert on auditing.
internal sealed class NoOpAuditService : IAuditService
{
    public Task LogAsync(string action, string entityType, string entityId, object? changes = null, Guid? actorUserId = null) => Task.CompletedTask;

    public Task<IReadOnlyCollection<TenantAuditLogItem>> GetTenantAuditLogsAsync(int page = 1, int pageSize = 50, string? action = null, DateTime? fromUtc = null, DateTime? toUtc = null)
        => Task.FromResult<IReadOnlyCollection<TenantAuditLogItem>>(Array.Empty<TenantAuditLogItem>());
}
