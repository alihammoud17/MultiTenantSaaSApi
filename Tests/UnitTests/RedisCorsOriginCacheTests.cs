using Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Tests.Containers;

namespace Tests.UnitTests;

/// <summary>
/// Runs against a real Redis: <c>REDIS_TEST_CONNECTION_STRING</c> when set (e.g. <c>localhost:6379</c>), otherwise a
/// Testcontainers Redis. Skipped when neither is available, so <c>dotnet test</c> keeps working without Docker.
/// </summary>
[Collection(RedisCollection.Name)]
public class RedisCorsOriginCacheTests
{
    private readonly RedisContainerFixture _redisFixture;

    public RedisCorsOriginCacheTests(RedisContainerFixture redisFixture)
    {
        _redisFixture = redisFixture;
    }

    [Fact]
    public async Task CachedList_ShouldBeServedUntilInvalidated()
    {
        await using var redis = await ConnectOrSkipAsync();
        var cache = new RedisCorsOriginCache(redis, NullLogger<RedisCorsOriginCache>.Instance);
        var scope = $"test:{Guid.NewGuid():N}";
        var loads = 0;
        IReadOnlyCollection<string> current = ["https://first.example.com"];
        Task<IReadOnlyCollection<string>> Load(CancellationToken _) { loads++; return Task.FromResult(current); }

        (await cache.GetOrLoadAsync(scope, Load, TestContext.Current.CancellationToken)).Should().Equal("https://first.example.com");
        current = ["https://second.example.com"];
        (await cache.GetOrLoadAsync(scope, Load, TestContext.Current.CancellationToken)).Should().Equal("https://first.example.com");
        loads.Should().Be(1);

        await cache.InvalidateAsync(TestContext.Current.CancellationToken);

        (await cache.GetOrLoadAsync(scope, Load, TestContext.Current.CancellationToken)).Should().Equal("https://second.example.com");
        loads.Should().Be(2);
    }

    [Fact]
    public async Task LoadThatStartedBeforeInvalidation_ShouldNotBeServedAfterIt()
    {
        await using var redis = await ConnectOrSkipAsync();
        var cache = new RedisCorsOriginCache(redis, NullLogger<RedisCorsOriginCache>.Instance);
        var scope = $"test:{Guid.NewGuid():N}";

        // The origin is removed (and the cache invalidated) while this lookup is still reading the old list.
        await cache.GetOrLoadAsync(scope, async ct =>
        {
            await cache.InvalidateAsync(ct);
            return ["https://removed.example.com"];
        }, TestContext.Current.CancellationToken);

        var afterRemoval = await cache.GetOrLoadAsync(
            scope,
            _ => Task.FromResult<IReadOnlyCollection<string>>([]),
            TestContext.Current.CancellationToken);

        afterRemoval.Should().BeEmpty();
    }

    [Fact]
    public async Task UnreachableRedis_ShouldFallBackToTheDatabaseLoad()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 100;
        options.AsyncTimeout = 100;
        options.SyncTimeout = 100;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        await using var redis = await ConnectionMultiplexer.ConnectAsync(options);
        var cache = new RedisCorsOriginCache(redis, NullLogger<RedisCorsOriginCache>.Instance);

        var origins = await cache.GetOrLoadAsync(
            "unreachable",
            _ => Task.FromResult<IReadOnlyCollection<string>>(["https://app.example.com"]),
            TestContext.Current.CancellationToken);
        var invalidate = () => cache.InvalidateAsync(TestContext.Current.CancellationToken);

        origins.Should().Equal("https://app.example.com");
        await invalidate.Should().NotThrowAsync();
    }

    private Task<ConnectionMultiplexer> ConnectOrSkipAsync() => _redisFixture.ConnectAsync();
}
