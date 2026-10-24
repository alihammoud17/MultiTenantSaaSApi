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
