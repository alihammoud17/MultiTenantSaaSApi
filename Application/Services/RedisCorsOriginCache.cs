using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Net.Sockets;
using System.Text.Json;

namespace Application.Services
{
    /// <summary>
    /// Caches CORS origin lists in Redis under a generation number. Invalidation increments the generation instead of
    /// deleting keys, so a lookup that loaded from the database before a change can only write under the old
    /// generation, never resurrect a removed origin. Old generations expire after <see cref="EntryLifetime"/>.
    /// When Redis is unavailable every lookup loads from the database.
    /// </summary>
    public sealed class RedisCorsOriginCache : ICorsOriginCache
    {
        public const string GenerationKey = "cors:tenant-origins:generation";
        public static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(5);

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisCorsOriginCache> _logger;

        public RedisCorsOriginCache(IConnectionMultiplexer redis, ILogger<RedisCorsOriginCache> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public static string EntryKey(long generation, string scope) => $"cors:tenant-origins:{generation}:{scope}";

        public async Task<IReadOnlyCollection<string>> GetOrLoadAsync(
            string scope,
            Func<CancellationToken, Task<IReadOnlyCollection<string>>> load,
            CancellationToken cancellationToken = default)
        {
            IDatabase db;
            string key;
            try
            {
                db = _redis.GetDatabase();
                var generation = (long?)await db.StringGetAsync(GenerationKey) ?? 0;
                key = EntryKey(generation, scope);

                var cached = await db.StringGetAsync(key);
                if (cached.HasValue)
                    return JsonSerializer.Deserialize<string[]>(cached.ToString()) ?? [];
            }
            catch (Exception ex) when (IsCacheFailure(ex))
            {
                _logger.LogWarning(ex, "CORS origin cache read failed for scope {Scope}; loading from the database", scope);
                return await load(cancellationToken);
            }

            var origins = await load(cancellationToken);

            try
            {
                await db.StringSetAsync(key, JsonSerializer.Serialize(origins), EntryLifetime);
            }
            catch (Exception ex) when (IsCacheFailure(ex))
            {
                _logger.LogWarning(ex, "CORS origin cache write failed for scope {Scope}", scope);
            }

            return origins;
        }

        public async Task InvalidateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _redis.GetDatabase().StringIncrementAsync(GenerationKey);
            }
            catch (Exception ex) when (IsCacheFailure(ex))
            {
                // Cached lists then stay stale until they expire.
                _logger.LogWarning(ex, "CORS origin cache invalidation failed; cached origin lists expire within {Lifetime}", EntryLifetime);
            }
        }

        private static bool IsCacheFailure(Exception ex)
            => ex is RedisException or RedisTimeoutException or SocketException or JsonException;
    }
}
