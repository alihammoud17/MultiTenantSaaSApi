using Application.Services;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class EntitlementEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_ShouldUsePlanEntitlementByDefault()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Tenant A",
            Subdomain = $"tenant-a-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tenants.Add(tenant);
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = "plan-free",
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, "feature.billing.invoices.read");

        result.IsAllowed.Should().BeTrue();
        result.ResolvedFrom.Should().Be("Plan");
    }

    [Fact]
    public async Task EvaluateAsync_ShouldApplyOverridePrecedenceOverPlan()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Tenant B",
            Subdomain = $"tenant-b-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tenants.Add(tenant);
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = "plan-pro",
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            CreatedAt = DateTime.UtcNow
        });
        db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            EntitlementKey = "feature.billing.invoices.read",
            Value = "false",
            Reason = "Support rollback",
            Source = TenantEntitlementOverrideSource.SupportGrant,
            EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = new EntitlementEvaluator(db, BuildHttpContextAccessor());
        var result = await sut.EvaluateAsync(tenant.Id, "feature.billing.invoices.read");

        result.IsAllowed.Should().BeFalse();
        result.ResolvedFrom.Should().Be("Override");
    }

    private static IHttpContextAccessor BuildHttpContextAccessor()
    {
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = $"test-correlation-{Guid.NewGuid():N}" }
        };
    }
}
