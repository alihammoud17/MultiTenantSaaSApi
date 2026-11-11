using Domain.Outputs;

namespace Domain.Interfaces
{
    // Operator-driven tenant status changes. requestedAtUtc is the signed request timestamp; requests that are not
    // newer than the tenant's last status change are rejected so a replayed request cannot undo a later change.
    public interface ITenantLifecycleService
    {
        Task<TenantStatusChangeResult> SuspendAsync(
            Guid tenantId,
            DateTime requestedAtUtc,
            string reason,
            string? actor,
            CancellationToken cancellationToken = default);

        Task<TenantStatusChangeResult> ReactivateAsync(
            Guid tenantId,
            DateTime requestedAtUtc,
            string? reason,
            string? actor,
            CancellationToken cancellationToken = default);
    }
}
