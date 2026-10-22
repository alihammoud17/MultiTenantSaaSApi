using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public sealed class TenantCorsOriginService : ITenantCorsOriginService
    {
        public const int MaxOriginsPerTenant = 20;

        internal const string AllTenantsScope = "all";
        internal static string SubdomainScope(string subdomain) => $"subdomain:{subdomain}";

        private readonly ApplicationDbContext _dbContext;
        private readonly ICorsOriginCache _cache;
        private readonly IAuditService _auditService;
        private readonly ILogger<TenantCorsOriginService> _logger;

        public TenantCorsOriginService(
            ApplicationDbContext dbContext,
            ICorsOriginCache cache,
            IAuditService auditService,
            ILogger<TenantCorsOriginService> logger)
        {
            _dbContext = dbContext;
            _cache = cache;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<TenantCorsOriginView>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.TenantAllowedOrigins
                .AsNoTracking()
                .Where(o => o.TenantId == tenantId)
                .OrderBy(o => o.Origin)
                .Select(o => new TenantCorsOriginView(o.Id, o.Origin, o.CreatedAt))
                .ToListAsync(cancellationToken);
        }

        public async Task<TenantCorsOriginAddResult> AddAsync(Guid tenantId, string? origin, bool allowLocalhost, CancellationToken cancellationToken = default)
        {
            if (!TenantCorsOriginValidator.TryNormalize(origin, allowLocalhost, out var normalized, out var error))
                return new TenantCorsOriginAddResult(TenantCorsOriginAddOutcome.Invalid, null, error);

            var existing = await _dbContext.TenantAllowedOrigins
                .Where(o => o.TenantId == tenantId)
                .Select(o => o.Origin)
                .ToListAsync(cancellationToken);

            if (existing.Contains(normalized, StringComparer.Ordinal))
                return new TenantCorsOriginAddResult(TenantCorsOriginAddOutcome.Duplicate, null, "Origin is already registered for this tenant.");

            if (existing.Count >= MaxOriginsPerTenant)
                return new TenantCorsOriginAddResult(TenantCorsOriginAddOutcome.LimitReached, null, $"A tenant can register at most {MaxOriginsPerTenant} origins.");

            var entity = new TenantAllowedOrigin
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Origin = normalized,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.TenantAllowedOrigins.Add(entity);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                if (!await IsRegisteredAsync(tenantId, normalized, cancellationToken))
                    throw;

                // A concurrent request registered the same origin first; the unique index rejected this one.
                return new TenantCorsOriginAddResult(TenantCorsOriginAddOutcome.Duplicate, null, "Origin is already registered for this tenant.");
            }

            await _cache.InvalidateAsync(cancellationToken);
            await _auditService.LogAsync("CORS_ORIGIN_ADDED", nameof(TenantAllowedOrigin), entity.Id.ToString(), new { entity.Origin });
            _logger.LogInformation("Tenant {TenantId} registered CORS origin {Origin}", tenantId, entity.Origin);

            return new TenantCorsOriginAddResult(
                TenantCorsOriginAddOutcome.Added,
                new TenantCorsOriginView(entity.Id, entity.Origin, entity.CreatedAt),
                null);
        }

        public async Task<bool> RemoveAsync(Guid tenantId, Guid originId, CancellationToken cancellationToken = default)
        {
            var entity = await _dbContext.TenantAllowedOrigins
                .SingleOrDefaultAsync(o => o.TenantId == tenantId && o.Id == originId, cancellationToken);
            if (entity is null)
                return false;

            _dbContext.TenantAllowedOrigins.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _cache.InvalidateAsync(cancellationToken);
            await _auditService.LogAsync("CORS_ORIGIN_REMOVED", nameof(TenantAllowedOrigin), entity.Id.ToString(), new { entity.Origin });
            _logger.LogInformation("Tenant {TenantId} removed CORS origin {Origin}", tenantId, entity.Origin);

            return true;
        }

        public async Task<bool> IsOriginAllowedAsync(string origin, string? tenantSubdomain, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(origin))
                return false;

            var origins = tenantSubdomain is null
                ? await _cache.GetOrLoadAsync(AllTenantsScope, LoadAllActiveTenantOriginsAsync, cancellationToken)
                : await _cache.GetOrLoadAsync(
                    SubdomainScope(tenantSubdomain),
                    ct => LoadActiveTenantOriginsAsync(tenantSubdomain, ct),
                    cancellationToken);

            // Stored origins are canonical, and browsers send canonical origins, so an exact match is required.
            return origins.Contains(origin, StringComparer.Ordinal);
        }

        public Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
            => _cache.InvalidateAsync(cancellationToken);

        // Preflight requests carry no credentials, so without a tenant host the lookup spans every active tenant.
        private async Task<IReadOnlyCollection<string>> LoadAllActiveTenantOriginsAsync(CancellationToken cancellationToken)
        {
            return await _dbContext.TenantAllowedOrigins
                .AsNoTracking()
                .Where(o => _dbContext.Tenants.Any(t => t.Id == o.TenantId && t.Status == TenantStatus.Active))
                .Select(o => o.Origin)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        private async Task<IReadOnlyCollection<string>> LoadActiveTenantOriginsAsync(string subdomain, CancellationToken cancellationToken)
        {
            return await _dbContext.TenantAllowedOrigins
                .AsNoTracking()
                .Where(o => _dbContext.Tenants.Any(t =>
                    t.Id == o.TenantId && t.Subdomain == subdomain && t.Status == TenantStatus.Active))
                .Select(o => o.Origin)
                .ToListAsync(cancellationToken);
        }

        private Task<bool> IsRegisteredAsync(Guid tenantId, string origin, CancellationToken cancellationToken)
            => _dbContext.TenantAllowedOrigins
                .AsNoTracking()
                .AnyAsync(o => o.TenantId == tenantId && o.Origin == origin, cancellationToken);
    }
}
