using Domain.Entities;
using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class TenantLifecycleService : ITenantLifecycleService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IAuditService _auditService;
        private readonly ITenantCorsOriginService _corsOriginService;
        private readonly ILogger<TenantLifecycleService> _logger;

        public TenantLifecycleService(
            ApplicationDbContext dbContext,
            ITenantContext tenantContext,
            IRefreshTokenService refreshTokenService,
            IAuditService auditService,
            ITenantCorsOriginService corsOriginService,
            ILogger<TenantLifecycleService> logger)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _refreshTokenService = refreshTokenService;
            _auditService = auditService;
            _corsOriginService = corsOriginService;
            _logger = logger;
        }

        public Task<TenantStatusChangeResult> SuspendAsync(Guid tenantId, DateTime requestedAtUtc, string reason, string? actor, CancellationToken cancellationToken = default)
        {
            return ChangeStatusAsync(tenantId, requestedAtUtc, TenantStatus.Active, TenantStatus.Suspended, reason, actor, cancellationToken);
        }

        public Task<TenantStatusChangeResult> ReactivateAsync(Guid tenantId, DateTime requestedAtUtc, string? reason, string? actor, CancellationToken cancellationToken = default)
        {
            return ChangeStatusAsync(tenantId, requestedAtUtc, TenantStatus.Suspended, TenantStatus.Active, reason, actor, cancellationToken);
        }

        private async Task<TenantStatusChangeResult> ChangeStatusAsync(
            Guid tenantId,
            DateTime requestedAtUtc,
            TenantStatus requiredStatus,
            TenantStatus newStatus,
            string? reason,
            string? actor,
            CancellationToken cancellationToken)
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
            if (tenant == null)
                return new TenantStatusChangeResult(TenantStatusChangeOutcome.TenantNotFound, null);

            if (tenant.StatusChangedAtUtc.HasValue && requestedAtUtc <= tenant.StatusChangedAtUtc.Value)
                return new TenantStatusChangeResult(TenantStatusChangeOutcome.StaleRequest, tenant.Status);

            if (tenant.Status != requiredStatus)
                return new TenantStatusChangeResult(TenantStatusChangeOutcome.InvalidTransition, tenant.Status);

            var previousStatus = tenant.Status;
            var suspending = newStatus == TenantStatus.Suspended;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            tenant.Status = newStatus;
            tenant.StatusChangedAtUtc = requestedAtUtc;
            tenant.SuspensionReason = suspending ? reason : null;
            tenant.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Refresh tokens and audit logs are tenant-filtered, so act inside the target tenant's context.
            _tenantContext.SetTenantId(tenant.Id);

            // Refresh already rejects non-active tenants; revoking means reactivation starts from clean sessions.
            var revokedTokens = suspending
                ? await _refreshTokenService.RevokeAllTenantTokensAsync(tenant.Id, "TENANT_SUSPENDED", cancellationToken)
                : 0;

            // The actor is supplied by whoever holds the operations secret; it is recorded, not authenticated.
            await _auditService.LogAsync(
                suspending ? "TENANT_SUSPENDED" : "TENANT_REACTIVATED",
                nameof(Tenant),
                tenant.Id.ToString(),
                new
                {
                    PreviousStatus = previousStatus.ToString(),
                    Status = newStatus.ToString(),
                    Reason = reason,
                    Actor = actor,
                    ActorAuthenticated = false,
                    RevokedRefreshTokens = revokedTokens
                });

            await transaction.CommitAsync(cancellationToken);

            // Only active tenants' origins are allowed for CORS, so cached origin lists change with the status.
            await _corsOriginService.InvalidateCacheAsync(cancellationToken);

            _logger.LogInformation("Tenant {TenantId} status changed from {PreviousStatus} to {Status} by operator request",
                tenant.Id, previousStatus, newStatus);

            return new TenantStatusChangeResult(TenantStatusChangeOutcome.Changed, newStatus);
        }
    }
}
