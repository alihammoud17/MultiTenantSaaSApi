using Application.Services;
using Domain.Entities;
using Domain.Outputs;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tests.Containers;
using Tests.UnitTests.Entitlements;

namespace Tests.UnitTests;

/// <summary>
/// Resolution rules of <see cref="EntitlementEvaluator"/>: add-on merge modes, effective windows, override
/// selection and tenant scoping. The same cases run on SQLite and, through Testcontainers, on PostgreSQL, because
/// the evaluator's single-round-trip query relies on provider translation of correlated collections.
/// </summary>
public abstract class EntitlementEvaluatorResolutionTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;

    protected abstract Task<ApplicationDbContext> CreateDbContextAsync();

    public async ValueTask InitializeAsync() => _db = await CreateDbContextAsync();

    public virtual async ValueTask DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Theory]
    [InlineData(AddOnEntitlementValueMode.Increment, "10", "5", "15")]
    [InlineData(AddOnEntitlementValueMode.Max, "10", "25", "25")]
    [InlineData(AddOnEntitlementValueMode.Max, "10", "3", "10")]
    [InlineData(AddOnEntitlementValueMode.Min, "10", "3", "3")]
    [InlineData(AddOnEntitlementValueMode.Min, "10", "25", "10")]
    [InlineData(AddOnEntitlementValueMode.Set, "10", "7", "7")]
    [InlineData(AddOnEntitlementValueMode.Increment, "unlimited", "5", "5")]
    [InlineData(AddOnEntitlementValueMode.Increment, "10", "lots", "10")]
    public async Task IntegerAddOn_ShouldMergeWithThePlanValue(AddOnEntitlementValueMode mode, string planValue, string addOnValue, string expected)
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("seats", EntitlementValueType.Integer, defaultValue: "1");
        scenario.PlanValue(key, planValue).AddOn(key, mode, addOnValue);

        var result = await EvaluateAsync(scenario, key);

        result.Value.Should().Be(expected);
        result.ResolvedFrom.Should().Be("AddOn");
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task DecimalAddOns_ShouldApplyInAssignmentOrder_OnTopOfTheDefinitionDefault()
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("storage", EntitlementValueType.Decimal, defaultValue: "1.5");
        scenario
            .AddOn(key, AddOnEntitlementValueMode.Increment, "2.25")
            .AddOn(key, AddOnEntitlementValueMode.Min, "3.5")
            .AddOn(key, AddOnEntitlementValueMode.Increment, "0.5");

        var result = await EvaluateAsync(scenario, key);

        // ((1.5 + 2.25) min 3.5) + 0.5
        result.Value.Should().Be("4.0");
        result.ResolvedFrom.Should().Be("AddOn");
    }

    [Fact]
    public async Task BooleanAddOn_ShouldReplaceThePlanValue_WhateverItsMode()
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("feature", EntitlementValueType.Boolean, defaultValue: "false");
        scenario.PlanValue(key, "false").AddOn(key, AddOnEntitlementValueMode.Increment, "true");

        var result = await EvaluateAsync(scenario, key);

        result.Value.Should().Be("true");
        result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(TenantAddOnAssignmentStatus.Active, 1, null)]   // not started yet
    [InlineData(TenantAddOnAssignmentStatus.Active, -2, -1)]    // already ended
    [InlineData(TenantAddOnAssignmentStatus.Scheduled, -1, null)]
    [InlineData(TenantAddOnAssignmentStatus.Canceled, -1, null)]
    [InlineData(TenantAddOnAssignmentStatus.Expired, -1, null)]
    public async Task AddOnsOutsideTheirWindowOrInactive_ShouldBeIgnored(TenantAddOnAssignmentStatus status, int startsInHours, int? endsInHours)
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("seats", EntitlementValueType.Integer, defaultValue: "1");
        scenario.PlanValue(key, "10").AddOn(
            key,
            AddOnEntitlementValueMode.Increment,
            "5",
            status,
            startsIn: TimeSpan.FromHours(startsInHours),
            endsIn: endsInHours is null ? null : TimeSpan.FromHours(endsInHours.Value));

        var result = await EvaluateAsync(scenario, key);

        result.Value.Should().Be("10");
        result.ResolvedFrom.Should().Be("Plan");
    }

    [Fact]
    public async Task MostRecentActiveOverride_ShouldWin_OverExpiredFutureAndOlderOverrides()
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("seats", EntitlementValueType.Integer, defaultValue: "1");
        scenario
            .PlanValue(key, "10")
            .AddOn(key, AddOnEntitlementValueMode.Increment, "5")
            .Override(key, "20", startedAgo: TimeSpan.FromDays(2))
            .Override(key, "30", startedAgo: TimeSpan.FromDays(1))
            .Override(key, "99", startedAgo: TimeSpan.FromHours(1), endsIn: TimeSpan.FromMinutes(-1))
            .Override(key, "77", startedAgo: TimeSpan.FromHours(-1));

        var result = await EvaluateAsync(scenario, key);

        result.Value.Should().Be("30");
        result.ResolvedFrom.Should().Be("Override");
    }

    [Fact]
    public async Task BlankMostRecentOverride_ShouldFallBackToTheLowerPrecedenceValue()
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("feature", EntitlementValueType.Boolean, defaultValue: "false");
        scenario
            .PlanValue(key, "true")
            .Override(key, "false", startedAgo: TimeSpan.FromDays(2))
            .Override(key, " ", startedAgo: TimeSpan.FromHours(1));

        var result = await EvaluateAsync(scenario, key);

        result.Value.Should().Be("true");
        result.ResolvedFrom.Should().Be("Plan");
    }

    [Fact]
    public async Task AnotherTenantsOverridesAndAddOns_ShouldNotApply()
    {
        var scenario = EntitlementScenario.Create(_db);
        var other = EntitlementScenario.Create(_db);
        var key = scenario.Define("feature", EntitlementValueType.Boolean, defaultValue: "false");
        other.AddOn(key, AddOnEntitlementValueMode.Set, "true");
        scenario.Override(key, "true", startedAgo: TimeSpan.FromHours(1), tenantId: other.Tenant.Id);

        var result = await EvaluateAsync(scenario, key);
        var otherResult = await EvaluateAsync(other, key);

        result.IsAllowed.Should().BeFalse();
        result.ResolvedFrom.Should().Be("Default");
        otherResult.IsAllowed.Should().BeTrue();
        otherResult.ResolvedFrom.Should().Be("Override");
    }

    [Fact]
    public async Task InactiveDefinition_ShouldNotSupplyADefault_AndBooleanParsingShouldApply()
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("retired", EntitlementValueType.Integer, defaultValue: "50", isActive: false);

        var withoutPlanValue = await EvaluateAsync(scenario, key);

        withoutPlanValue.Value.Should().BeNull();
        withoutPlanValue.ResolvedFrom.Should().Be("DefaultDeny");
        withoutPlanValue.IsAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(SubscriptionStatus.GracePeriod)]
    [InlineData(SubscriptionStatus.Canceled)]
    public async Task SubscriptionStatus_ShouldBeReported_WithTheResolvedValue(SubscriptionStatus status)
    {
        var scenario = EntitlementScenario.Create(_db, status);
        var key = scenario.Define("feature", EntitlementValueType.Boolean, defaultValue: "false");
        scenario.PlanValue(key, "true");

        var result = await EvaluateAsync(scenario, key);

        result.SubscriptionStatus.Should().Be(status.ToString());
        result.Value.Should().Be("true");
    }

    [Fact]
    public async Task EvaluateMany_ShouldResolveEachKeyIndependently_AndIgnoreDuplicates()
    {
        var scenario = EntitlementScenario.Create(_db);
        var planKey = scenario.Define("plan", EntitlementValueType.Boolean, defaultValue: "false");
        var addOnKey = scenario.Define("addon", EntitlementValueType.Integer, defaultValue: "1");
        var overrideKey = scenario.Define("override", EntitlementValueType.Boolean, defaultValue: "false");
        var defaultKey = scenario.Define("default", EntitlementValueType.String, defaultValue: "eu-west");
        var unknownKey = $"unknown-{Guid.NewGuid():N}";
        scenario
            .PlanValue(planKey, "true")
            .AddOn(addOnKey, AddOnEntitlementValueMode.Increment, "4")
            .Override(overrideKey, "true", startedAgo: TimeSpan.FromMinutes(5));
        await _db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor());
        var results = await sut.EvaluateManyAsync(scenario.Tenant.Id, [planKey, addOnKey, overrideKey, defaultKey, unknownKey, planKey]);

        results.Should().HaveCount(5);
        results[planKey].ResolvedFrom.Should().Be("Plan");
        results[addOnKey].Value.Should().Be("5");
        results[overrideKey].ResolvedFrom.Should().Be("Override");
        results[defaultKey].Should().Match<EntitlementEvaluationResult>(r => r.Value == "eu-west" && r.ResolvedFrom == "Default" && r.IsAllowed);
        results[unknownKey].Should().Match<EntitlementEvaluationResult>(r => r.Value == null && r.ResolvedFrom == "DefaultDeny" && !r.IsAllowed);
        results.Values.Should().AllSatisfy(r => r.CorrelationId.Should().BeNull("there is no HTTP request"));
    }

    [Fact]
    public async Task WithinARequest_ResultsShouldBeMemoizedPerTenantAndKey()
    {
        var scenario = EntitlementScenario.Create(_db);
        var key = scenario.Define("feature", EntitlementValueType.Boolean, defaultValue: "false");
        await _db.SaveChangesAsync();
        var sut = new EntitlementEvaluator(_db, new HttpContextAccessor { HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-1" } });

        var first = await sut.EvaluateAsync(scenario.Tenant.Id, key);
        scenario.Override(key, "true", startedAgo: TimeSpan.FromMinutes(1));
        await _db.SaveChangesAsync();
        var second = await sut.EvaluateAsync(scenario.Tenant.Id, key);
        var fresh = await new EntitlementEvaluator(_db, new HttpContextAccessor()).EvaluateAsync(scenario.Tenant.Id, key);

        first.CorrelationId.Should().Be("trace-1");
        second.Should().BeSameAs(first);
        fresh.IsAllowed.Should().BeTrue();
        (await sut.EvaluateManyAsync(scenario.Tenant.Id, [])).Should().BeEmpty();
    }

    private async Task<EntitlementEvaluationResult> EvaluateAsync(EntitlementScenario scenario, string key)
    {
        await _db.SaveChangesAsync();
        return await new EntitlementEvaluator(_db, new HttpContextAccessor()).EvaluateAsync(scenario.Tenant.Id, key);
    }
}

public sealed class SqliteEntitlementEvaluatorResolutionTests : EntitlementEvaluatorResolutionTests
{
    private SqliteConnection? _connection;

    protected override async Task<ApplicationDbContext> CreateDbContextAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlEntitlementEvaluatorResolutionTests(PostgreSqlContainerFixture fixture) : EntitlementEvaluatorResolutionTests
{
    protected override async Task<ApplicationDbContext> CreateDbContextAsync() =>
        PostgreSqlContainerFixture.CreateDbContext(await fixture.CreateDatabaseAsync());
}
