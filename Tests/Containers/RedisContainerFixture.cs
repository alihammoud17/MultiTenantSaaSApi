using StackExchange.Redis;
using Testcontainers.Redis;

namespace Tests.Containers;

/// <summary>
/// Provides a real Redis for one test collection. Uses <c>REDIS_TEST_CONNECTION_STRING</c> when it is set, otherwise
/// starts a Redis container. When neither is available the tests are skipped rather than failed.
/// Tests share the instance, so they must use their own keys (e.g. a fresh tenant id per test).
/// </summary>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    public const string ConnectionStringVariable = "REDIS_TEST_CONNECTION_STRING";

    private static readonly string Image =
        Environment.GetEnvironmentVariable("TESTCONTAINERS_REDIS_IMAGE") ?? "redis:7-alpine";

    private RedisContainer? _container;
    private string? _connectionString;
    private string? _unavailableReason;

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _connectionString = configured;
            return;
        }

        RedisContainer? container = null;
        try
        {
            // Build() already fails when no Docker endpoint is reachable.
            container = new RedisBuilder(Image).Build();
            await container.StartAsync();
        }
        catch (Exception ex)
        {
            _unavailableReason = $"{ConnectionStringVariable} is not set and a Redis container could not start (is Docker running?): {ex.GetBaseException().Message}";
            if (container is not null)
                await container.DisposeAsync();
            return;
        }

        _container = container;
        _connectionString = container.GetConnectionString();
    }

    public Task<ConnectionMultiplexer> ConnectAsync()
    {
        Assert.SkipWhen(_connectionString is null, _unavailableReason ?? "Redis is not available.");
        return ConnectionMultiplexer.ConnectAsync(_connectionString!);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "Redis container";
}
