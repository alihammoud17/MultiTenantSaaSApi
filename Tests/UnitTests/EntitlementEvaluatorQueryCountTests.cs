using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tests.UnitTests.Entitlements;

namespace Tests.UnitTests;

/// <summary>
/// Pins how many database round trips entitlement evaluation makes, so request-path query regressions fail a test
/// instead of surfacing as latency. Counts come from SQLite; PostgreSQL issues the same number of commands.
/// </summary>
public class EntitlementEvaluatorQueryCountTests : IAsyncLifetime
{
    private const string PlanId = "query-count-plan";
    private const string AddOnId = "addon.query-count";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly QueryCountingInterceptor _queries = new();
    private ApplicationDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_queries)
            .Options;
        _db = new ApplicationDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateAsync_ShouldUseThreeQueries_WhenPlanAddOnAndOverrideAllContribute()
    {
        var tenantId = await SeedTenantAsync(withSubscription: true, "query.count.single");
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());
        _queries.Reset();

        var result = await sut.EvaluateAsync(tenantId, "query.count.single", TestContext.Current.CancellationToken);

        result.ResolvedFrom.Should().Be("Override");
        _queries.Count.Should().Be(3, "subscription snapshot (plan value + override), definition, and add-on contributions");
    }

    [Fact]
    public async Task EvaluateAsync_ShouldUseOneQuery_WhenTenantHasNoSubscription()
    {
        var tenantId = await SeedTenantAsync(withSubscription: false, "query.count.nosub");
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());
        _queries.Reset();

        var result = await sut.EvaluateAsync(tenantId, "query.count.nosub", TestContext.Current.CancellationToken);

        result.SubscriptionStatus.Should().Be("MissingSubscription");
        _queries.Count.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateManyAsync_ShouldUseThreeQueries_RegardlessOfKeyCount()
    {
        string[] keys = ["query.count.a", "query.count.b", "query.count.c", "query.count.d", "query.count.e"];
        var tenantId = await SeedTenantAsync(withSubscription: true, keys);
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());
        _queries.Reset();

        var results = await sut.EvaluateManyAsync(tenantId, keys, TestContext.Current.CancellationToken);

        results.Keys.Should().BeEquivalentTo(keys);
        _queries.Count.Should().Be(3);
    }

    [Fact]
    public async Task EvaluateManyAsync_ShouldMatchSingleKeyEvaluation_ForEveryKey()
    {
        string[] keys = ["query.count.match.a", "query.count.match.b"];
        var tenantId = await SeedTenantAsync(withSubscription: true, keys);
        const string unknownKey = "query.count.match.undefined";
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());

        var batch = await sut.EvaluateManyAsync(tenantId, [.. keys, unknownKey], TestContext.Current.CancellationToken);

        foreach (var key in keys.Append(unknownKey))
        {
            var single = await sut.EvaluateAsync(tenantId, key, TestContext.Current.CancellationToken);
            batch[key].Should().BeEquivalentTo(single, options => options.Excluding(r => r.EvaluatedAtUtc));
        }

        batch[keys[0]].ResolvedFrom.Should().Be("Override");
        batch[unknownKey].ResolvedFrom.Should().Be("DefaultDeny");
        batch[unknownKey].IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateManyAsync_ShouldUseOneQuery_AndDenyEveryKey_WhenTenantHasNoSubscription()
    {
        string[] keys = ["query.count.nosub.a", "query.count.nosub.b"];
        var tenantId = await SeedTenantAsync(withSubscription: false, keys);
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());
        _queries.Reset();

        var results = await sut.EvaluateManyAsync(tenantId, keys, TestContext.Current.CancellationToken);

        _queries.Count.Should().Be(1);
        results.Values.Should().AllSatisfy(r =>
        {
            r.IsAllowed.Should().BeFalse();
            r.ResolvedFrom.Should().Be("DefaultDeny");
            r.SubscriptionStatus.Should().Be("MissingSubscription");
        });
    }

    [Fact]
    public async Task EvaluateManyAsync_ShouldCollapseDuplicateKeys_AndSkipTheDatabaseForNoKeys()
    {
        var tenantId = await SeedTenantAsync(withSubscription: true, "query.count.dup");
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());
        _queries.Reset();

        var none = await sut.EvaluateManyAsync(tenantId, [], TestContext.Current.CancellationToken);
        _queries.Count.Should().Be(0);

        var duplicates = await sut.EvaluateManyAsync(tenantId, ["query.count.dup", "query.count.dup"], TestContext.Current.CancellationToken);

        none.Should().BeEmpty();
        duplicates.Keys.Should().Equal("query.count.dup");
    }

    [Fact]
    public async Task EvaluateManyAsync_ShouldNotUseAnotherTenantsOverridesOrAddOns()
    {
        const string key = "query.count.isolation";
        var tenantWithOverride = await SeedTenantAsync(withSubscription: true, key);
        var otherTenant = await SeedSubscribedTenantWithoutSourcesAsync();
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());

        var own = await sut.EvaluateManyAsync(tenantWithOverride, [key], TestContext.Current.CancellationToken);
        var other = await sut.EvaluateManyAsync(otherTenant, [key], TestContext.Current.CancellationToken);

        own[key].ResolvedFrom.Should().Be("Override");
        own[key].IsAllowed.Should().BeTrue();
        other[key].ResolvedFrom.Should().Be("Plan", "the other tenant shares the plan but has no add-on or override");
        other[key].IsAllowed.Should().BeFalse();
    }

    private async Task<Guid> SeedSubscribedTenantWithoutSourcesAsync()
    {
        var baseline = EntitlementMatrixFixtureBuilder.BaselineUtc;
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Other Tenant", Subdomain = $"other-{Guid.NewGuid():N}", CreatedAt = baseline, UpdatedAt = baseline };
        _db.Tenants.Add(tenant);
        _db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = PlanId,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = baseline,
            CurrentPeriodEnd = baseline.AddMonths(1),
            CreatedAt = baseline
        });
        await _db.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>
    /// Seeds a tenant whose every key has a plan value (<c>false</c>), a <c>Set</c> add-on (<c>false</c>) and an
    /// active override (<c>true</c>), so each key exercises every evaluation source.
    /// </summary>
    private async Task<Guid> SeedTenantAsync(bool withSubscription, params string[] keys)
    {
        var baseline = EntitlementMatrixFixtureBuilder.BaselineUtc;
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Query Count Tenant",
            Subdomain = $"query-count-{Guid.NewGuid():N}",
            CreatedAt = baseline,
            UpdatedAt = baseline
        };
        _db.Tenants.Add(tenant);
        _db.Plans.Add(new Plan { Id = PlanId, Name = "Query Count Plan", ApiCallsPerMonth = 1000, MaxUsers = 5, IsActive = true, DisplayOrder = 1 });
        _db.AddOnDefinitions.Add(new AddOnDefinition { Id = AddOnId, DisplayName = "Query Count Add-on", IsActive = true, CreatedUtc = baseline, UpdatedUtc = baseline });
        _db.TenantAddOnAssignments.Add(new TenantAddOnAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            AddOnId = AddOnId,
            Status = TenantAddOnAssignmentStatus.Active,
            EffectiveFromUtc = baseline.AddMinutes(-5),
            CreatedUtc = baseline,
            UpdatedUtc = baseline
        });

        if (withSubscription)
        {
            _db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                PlanId = PlanId,
                Status = SubscriptionStatus.Active,
                CurrentPeriodStart = baseline,
                CurrentPeriodEnd = baseline.AddMonths(1),
                CreatedAt = baseline
            });
        }

        foreach (var key in keys)
        {
            _db.EntitlementDefinitions.Add(new EntitlementDefinition
            {
                Key = key,
                DisplayName = key,
                ValueType = EntitlementValueType.Boolean,
                Category = EntitlementCategory.Feature,
                DefaultValue = "false",
                IsActive = true,
                CreatedUtc = baseline,
                UpdatedUtc = baseline
            });
            _db.PlanEntitlements.Add(new PlanEntitlement { PlanId = PlanId, EntitlementKey = key, Value = "false", Source = EntitlementSourceType.PlanDefault, CreatedUtc = baseline, UpdatedUtc = baseline });
            _db.AddOnEntitlements.Add(new AddOnEntitlement { AddOnId = AddOnId, EntitlementKey = key, ValueMode = AddOnEntitlementValueMode.Set, Value = "false", CreatedUtc = baseline, UpdatedUtc = baseline });
            _db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                EntitlementKey = key,
                Value = "true",
                Reason = "Query count fixture",
                Source = TenantEntitlementOverrideSource.SupportGrant,
                EffectiveFromUtc = baseline.AddMinutes(-1),
                CreatedUtc = baseline
            });
        }

        await _db.SaveChangesAsync();
        return tenant.Id;
    }
}
