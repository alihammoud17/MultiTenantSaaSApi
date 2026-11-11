using System.Collections.Concurrent;
using Domain.Interfaces;

namespace Tests.Integration;

/// <summary>
/// Stands in for the Redis-backed cache in integration tests. It caches until invalidated, like Redis does, so a
/// missing invalidation shows up as a stale CORS decision instead of being hidden by a fresh database read.
/// </summary>
public sealed class InMemoryCorsOriginCache : ICorsOriginCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<string>> _entries = new();

    public async Task<IReadOnlyCollection<string>> GetOrLoadAsync(
        string scope,
        Func<CancellationToken, Task<IReadOnlyCollection<string>>> load,
        CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(scope, out var cached))
            return cached;

        var origins = await load(cancellationToken);
        _entries[scope] = origins;
        return origins;
    }

    public Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        _entries.Clear();
        return Task.CompletedTask;
    }
}
