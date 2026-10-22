namespace Domain.Interfaces;

public interface ITenantCorsOriginService
{
    Task<IReadOnlyCollection<TenantCorsOriginView>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <param name="allowLocalhost">When true (Development only), <c>http://localhost[:port]</c> is accepted.</param>
    Task<TenantCorsOriginAddResult> AddAsync(Guid tenantId, string? origin, bool allowLocalhost, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid tenantId, Guid originId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="origin"/> is registered by an active tenant: the tenant served at
    /// <paramref name="tenantSubdomain"/> when the request host names one, otherwise any active tenant.
    /// </summary>
    Task<bool> IsOriginAllowedAsync(string origin, string? tenantSubdomain, CancellationToken cancellationToken = default);

    /// <summary>Drops cached origin lists, e.g. after a tenant status change.</summary>
    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
}

public sealed record TenantCorsOriginView(Guid Id, string Origin, DateTime CreatedAt);

public enum TenantCorsOriginAddOutcome
{
    Added,
    Invalid,
    Duplicate,
    LimitReached
}

public sealed record TenantCorsOriginAddResult(TenantCorsOriginAddOutcome Outcome, TenantCorsOriginView? Origin, string? Error);
