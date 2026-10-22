namespace Domain.Interfaces;

/// <summary>
/// Shared cache of the origin lists used by CORS policy evaluation. Implementations must fall back to
/// <c>load</c> when the cache is unavailable, so a cache outage never widens or blocks CORS on its own.
/// </summary>
public interface ICorsOriginCache
{
    Task<IReadOnlyCollection<string>> GetOrLoadAsync(
        string scope,
        Func<CancellationToken, Task<IReadOnlyCollection<string>>> load,
        CancellationToken cancellationToken = default);

    /// <summary>Discards every cached origin list; the next lookup reloads from the database.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
