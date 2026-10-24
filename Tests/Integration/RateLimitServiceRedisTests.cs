using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Tests.Containers;

namespace Tests.Integration;

/// <summary>
/// Exercises the plan-aware monthly limiter against a real Redis; the API integration suites replace it with an
/// always-allow fake, so its counting, expiry and failure behaviour is only covered here.
/// </summary>
[Collection(RedisCollection.Name)]
public sealed class RateLimitServiceRedisTests : IAsyncLifetime
{
    private readonly RedisContainerFixture _redisFixture;
    private ConnectionMultiplexer _redis = null!;
    private SqliteConnection _sqlite = null!;
    private ApplicationDbContext _db = null!;

    public RateLimitServiceRedisTests(RedisContainerFixture redisFixture)
    {
        _redisFixture = redisFixture;
    }

    public async ValueTask InitializeAsync()
    {
        _redis = await _redisFixture.ConnectAsync();

        _sqlite = new SqliteConnection("DataSource=:memory:");
        await _sqlite.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_sqlite).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_sqlite is not null) await _sqlite.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task FirstCall_ShouldCountUsage_AndExpireTheCounterAfterTheFollowingMonth()
    {
        var tenantId = Guid.NewGuid();

        var result = await CreateService(limit: 10).CheckRateLimitAsync(tenantId);

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(10);
        result.Remaining.Should().Be(9);
        result.ResetDate.Should().Be(monthStart.AddMonths(1));

        var key = CounterKey(tenantId);
        ((int)await _redis.GetDatabase().StringGetAsync(key)).Should().Be(1);
        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        (now + ttl!.Value).Should().BeCloseTo(monthStart.AddMonths(2), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CallsBeyondTheLimit_ShouldBeRejected_WithoutRaisingTheCounterFurther()
    {
        var tenantId = Guid.NewGuid();

        var results = new List<Domain.Outputs.RateLimitResult>();
        for (var i = 0; i < 5; i++)
            results.Add(await CreateService(limit: 3).CheckRateLimitAsync(tenantId));

        results.Select(r => r.IsAllowed).Should().Equal(true, true, true, false, false);
        results.Select(r => r.Remaining).Should().Equal(2, 1, 0, 0, 0);
        ((int)await _redis.GetDatabase().StringGetAsync(CounterKey(tenantId))).Should().Be(3);
    }

    [Fact]
    public async Task ConcurrentCalls_ShouldNeverAllowMoreThanThePlanLimit()
    {
        const int limit = 5;
        var tenantId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 60)
            .Select(_ => Task.Run(() => CreateService(limit).CheckRateLimitAsync(tenantId))));

        results.Count(r => r.IsAllowed).Should().Be(limit);
        results.Where(r => r.IsAllowed).Select(r => r.Remaining).Should().BeEquivalentTo(Enumerable.Range(0, limit));
    }

    [Fact]
    public async Task ExhaustingOneTenant_ShouldNotAffectAnotherTenant()
    {
        var exhausted = Guid.NewGuid();
        var other = Guid.NewGuid();

        await CreateService(limit: 1).CheckRateLimitAsync(exhausted);
        (await CreateService(limit: 1).CheckRateLimitAsync(exhausted)).IsAllowed.Should().BeFalse();

        (await CreateService(limit: 1).CheckRateLimitAsync(other)).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutPreloadedLimit_ShouldReadThePlanLimitFromTheSubscription_AndCacheItForTheRequest()
    {
        var tenantId = await SeedTenantWithSubscriptionAsync("plan-free");
        var accessContext = new RequestTenantAccessContext();
        var sut = new RateLimitService(_redis, _db, accessContext, NullLogger<RateLimitService>.Instance);

        var result = await sut.CheckRateLimitAsync(tenantId);

        var freePlanLimit = await _db.Plans.Where(p => p.Id == "plan-free").Select(p => p.ApiCallsPerMonth).SingleAsync();
        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(freePlanLimit);
        accessContext.ApiCallsPerMonthLimit.Should().Be(freePlanLimit);
    }

    [Fact]
    public async Task WithoutPreloadedLimitOrSubscription_ShouldThrow()
    {
        var sut = new RateLimitService(_redis, _db, new RequestTenantAccessContext(), NullLogger<RateLimitService>.Instance);

        var act = () => sut.CheckRateLimitAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no subscription*");
    }

    [Fact]
    public async Task UnreachableRedis_ShouldFailOpen_WithTheFullAllowanceReported()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 200;
        options.SyncTimeout = 200;
        options.AsyncTimeout = 200;
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var accessContext = new RequestTenantAccessContext();
        accessContext.SetApiCallsPerMonthLimit(7);
        var sut = new RateLimitService(unreachable, _db, accessContext, NullLogger<RateLimitService>.Instance);

        var result = await sut.CheckRateLimitAsync(Guid.NewGuid());

        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(7);
        result.Remaining.Should().Be(7);
    }

    private RateLimitService CreateService(int limit)
    {
        var accessContext = new RequestTenantAccessContext();
        accessContext.SetApiCallsPerMonthLimit(limit);
        return new RateLimitService(_redis, _db, accessContext, NullLogger<RateLimitService>.Instance);
    }

    private static string CounterKey(Guid tenantId) => $"ratelimit:{tenantId}:{DateTime.UtcNow:yyyy-MM}";

    private async Task<Guid> SeedTenantWithSubscriptionAsync(string planId)
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Rate Limit Tenant", Subdomain = $"rl-{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now };
        _db.Tenants.Add(tenant);
        _db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddMonths(1),
            CreatedAt = now
        });
        await _db.SaveChangesAsync();
        return tenant.Id;
    }
}
