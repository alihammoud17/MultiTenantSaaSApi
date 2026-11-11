using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tests.UnitTests.Entitlements;

namespace Tests.UnitTests;

public class EntitlementEvaluatorTests
{
    [Theory]
    [MemberData(nameof(EntitlementMatrixFixtureBuilder.BooleanCases), MemberType = typeof(EntitlementMatrixFixtureBuilder))]
    public async Task EvaluateAsync_ShouldResolveBooleanEntitlementFromMatrixCases(EntitlementMatrixCase matrixCase)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = SeedMatrixCase(db, matrixCase);
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, matrixCase.EntitlementKey);

        EntitlementMatrixAssertions.AssertMatchesExpectation(result, matrixCase);
    }

    [Theory]
    [MemberData(nameof(EntitlementMatrixFixtureBuilder.NumericCases), MemberType = typeof(EntitlementMatrixFixtureBuilder))]
    public async Task EvaluateAsync_ShouldResolveNumericEntitlementFromMatrixCases(EntitlementMatrixCase matrixCase)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = SeedMatrixCase(db, matrixCase);
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, matrixCase.EntitlementKey);

        EntitlementMatrixAssertions.AssertMatchesExpectation(result, matrixCase);
    }

    [Theory]
    [MemberData(nameof(EntitlementMatrixFixtureBuilder.EndpointGateRegressionCases), MemberType = typeof(EntitlementMatrixFixtureBuilder))]
    public async Task EvaluateAsync_ShouldResolveEndpointGateEntitlementsAcrossHighValueMatrixCases(EntitlementMatrixCase matrixCase)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = SeedMatrixCase(db, matrixCase);
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, matrixCase.EntitlementKey);

        EntitlementMatrixAssertions.AssertMatchesExpectation(result, matrixCase);
        result.SubscriptionStatus.Should().Be(matrixCase.SubscriptionStatus.ToString());
    }

    [Fact]
    public async Task EvaluateAsync_ShouldDefaultDeny_WhenTenantSubscriptionMissing()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "No Subscription Tenant",
            Subdomain = $"tenant-no-sub-{Guid.NewGuid():N}",
            CreatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, "feature.billing.invoices.read");

        result.IsAllowed.Should().BeFalse();
        result.ResolvedFrom.Should().Be("DefaultDeny");
        result.SubscriptionStatus.Should().Be("MissingSubscription");
    }

    [Fact]
    public async Task EvaluateAsync_ShouldPreservePlanAddOnOverridePrecedence_ForBooleanEntitlements()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Precedence Tenant",
            Subdomain = $"precedence-{Guid.NewGuid():N}",
            CreatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc
        };
        db.Tenants.Add(tenant);

        const string entitlementKey = "matrix.feature.precedence.boolean";
        const string planId = "matrix-plan-precedence";

        db.Plans.Add(new Plan
        {
            Id = planId,
            Name = "Precedence Plan",
            MonthlyPrice = 0m,
            ApiCallsPerMonth = 1000,
            MaxUsers = 5,
            IsActive = true,
            DisplayOrder = 1
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = EntitlementMatrixFixtureBuilder.BaselineUtc,
            CurrentPeriodEnd = EntitlementMatrixFixtureBuilder.BaselineUtc.AddMonths(1),
            CreatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc
        });
        db.EntitlementDefinitions.Add(new EntitlementDefinition
        {
            Key = entitlementKey,
            DisplayName = entitlementKey,
            ValueType = EntitlementValueType.Boolean,
            Category = EntitlementCategory.Feature,
            DefaultValue = "false",
            IsActive = true,
            CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
        });
        db.PlanEntitlements.Add(new PlanEntitlement
        {
            PlanId = planId,
            EntitlementKey = entitlementKey,
            Value = "true",
            Source = EntitlementSourceType.PlanDefault,
            CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
        });

        db.AddOnDefinitions.Add(new AddOnDefinition
        {
            Id = "addon.precedence.boolean",
            DisplayName = "Precedence Add-on",
            IsActive = true,
            CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
        });
        db.AddOnEntitlements.Add(new AddOnEntitlement
        {
            AddOnId = "addon.precedence.boolean",
            EntitlementKey = entitlementKey,
            ValueMode = AddOnEntitlementValueMode.Set,
            Value = "false",
            CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
        });
        db.TenantAddOnAssignments.Add(new TenantAddOnAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            AddOnId = "addon.precedence.boolean",
            Status = TenantAddOnAssignmentStatus.Active,
            EffectiveFromUtc = EntitlementMatrixFixtureBuilder.BaselineUtc.AddMinutes(-5),
            EffectiveToUtc = null,
            CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
        });

        db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            EntitlementKey = entitlementKey,
            Value = "true",
            Reason = "Precedence regression assertion",
            Source = TenantEntitlementOverrideSource.SupportGrant,
            EffectiveFromUtc = EntitlementMatrixFixtureBuilder.BaselineUtc.AddMinutes(-1),
            CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
        });
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, entitlementKey);

        result.Value.Should().Be("true");
        result.IsAllowed.Should().BeTrue();
        result.ResolvedFrom.Should().Be("Override");
    }

    [Fact]
    public async Task EvaluateAsync_ShouldDefaultDeny_WhenDefinitionAndSourceValuesAreMissing()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "No Definition Tenant",
            Subdomain = $"nodef-{Guid.NewGuid():N}",
            CreatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc
        };
        db.Tenants.Add(tenant);

        const string planId = "matrix-plan-nodef";
        const string entitlementKey = "matrix.feature.defaultdeny.nodef";
        db.Plans.Add(new Plan
        {
            Id = planId,
            Name = "No Definition Plan",
            MonthlyPrice = 0m,
            ApiCallsPerMonth = 1000,
            MaxUsers = 5,
            IsActive = true,
            DisplayOrder = 1
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = EntitlementMatrixFixtureBuilder.BaselineUtc,
            CurrentPeriodEnd = EntitlementMatrixFixtureBuilder.BaselineUtc.AddMonths(1),
            CreatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc
        });
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, entitlementKey);

        result.Value.Should().BeNull();
        result.IsAllowed.Should().BeFalse();
        result.ResolvedFrom.Should().Be("DefaultDeny");
    }

    private static Tenant SeedMatrixCase(ApplicationDbContext db, EntitlementMatrixCase matrixCase)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = $"Matrix Tenant {matrixCase.Name}",
            Subdomain = $"matrix-{matrixCase.Name}-{Guid.NewGuid():N}",
            CreatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc,
            UpdatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc
        };

        db.Tenants.Add(tenant);
        if (!db.Plans.Any(x => x.Id == matrixCase.PlanId))
        {
            db.Plans.Add(new Plan
            {
                Id = matrixCase.PlanId,
                Name = $"Matrix {matrixCase.PlanId}",
                MonthlyPrice = 0m,
                ApiCallsPerMonth = 1000,
                MaxUsers = 5,
                IsActive = true,
                DisplayOrder = 1
            });
        }
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = matrixCase.PlanId,
            Status = matrixCase.SubscriptionStatus,
            CurrentPeriodStart = EntitlementMatrixFixtureBuilder.BaselineUtc,
            CurrentPeriodEnd = EntitlementMatrixFixtureBuilder.BaselineUtc.AddMonths(1),
            CreatedAt = EntitlementMatrixFixtureBuilder.BaselineUtc
        });

        if (!db.EntitlementDefinitions.Any(x => x.Key == matrixCase.EntitlementKey))
        {
            db.EntitlementDefinitions.Add(new EntitlementDefinition
            {
                Key = matrixCase.EntitlementKey,
                DisplayName = matrixCase.EntitlementKey,
                ValueType = matrixCase.ValueType,
                Category = EntitlementCategory.Feature,
                DefaultValue = matrixCase.DefinitionDefaultValue,
                IsActive = true,
                CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
                UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
            });
        }

        if (!string.IsNullOrWhiteSpace(matrixCase.PlanValue))
        {
            if (!db.PlanEntitlements.Any(x => x.PlanId == matrixCase.PlanId && x.EntitlementKey == matrixCase.EntitlementKey))
            {
                db.PlanEntitlements.Add(new PlanEntitlement
                {
                    PlanId = matrixCase.PlanId,
                    EntitlementKey = matrixCase.EntitlementKey,
                    Value = matrixCase.PlanValue,
                    Source = EntitlementSourceType.PlanDefault,
                    CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
                    UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
                });
            }
        }

        foreach (var addOn in matrixCase.AddOns)
        {
            db.AddOnDefinitions.Add(new AddOnDefinition
            {
                Id = addOn.AddOnId,
                DisplayName = addOn.AddOnId,
                IsActive = true,
                CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
                UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
            });

            db.AddOnEntitlements.Add(new AddOnEntitlement
            {
                AddOnId = addOn.AddOnId,
                EntitlementKey = matrixCase.EntitlementKey,
                ValueMode = addOn.ValueMode,
                Value = addOn.Value,
                CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
                UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
            });

            db.TenantAddOnAssignments.Add(new TenantAddOnAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                AddOnId = addOn.AddOnId,
                Status = TenantAddOnAssignmentStatus.Active,
                EffectiveFromUtc = EntitlementMatrixFixtureBuilder.BaselineUtc.AddMinutes(-5),
                EffectiveToUtc = null,
                CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc,
                UpdatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
            });
        }

        if (!string.IsNullOrWhiteSpace(matrixCase.OverrideValue))
        {
            db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                EntitlementKey = matrixCase.EntitlementKey,
                Value = matrixCase.OverrideValue,
                Reason = "Matrix case override",
                Source = TenantEntitlementOverrideSource.SupportGrant,
                EffectiveFromUtc = EntitlementMatrixFixtureBuilder.BaselineUtc.AddMinutes(-1),
                CreatedUtc = EntitlementMatrixFixtureBuilder.BaselineUtc
            });
        }

        return tenant;
    }

    private static IHttpContextAccessor BuildHttpContextAccessor()
    {
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = $"test-correlation-{Guid.NewGuid():N}" }
        };
    }
}
