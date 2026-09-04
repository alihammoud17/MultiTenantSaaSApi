using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class DatabaseIndexConfigurationTests
{
    [Fact]
    public void Model_ShouldExposeQueryAlignedIndexesWithoutTenantContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var dbContext = new ApplicationDbContext(options);

        AssertIndex<AuditLog>(dbContext, false, nameof(AuditLog.TenantId), nameof(AuditLog.Timestamp));
        AssertIndex<AuditLog>(dbContext, false, nameof(AuditLog.TenantId), nameof(AuditLog.Action), nameof(AuditLog.Timestamp));
        AssertIndex<BillingEventInbox>(dbContext, false, nameof(BillingEventInbox.TenantId), nameof(BillingEventInbox.EventType), nameof(BillingEventInbox.OccurredAtUtc));
        AssertIndex<User>(dbContext, true, nameof(User.Email));
    }

    private static void AssertIndex<TEntity>(ApplicationDbContext dbContext, bool unique, params string[] propertyNames)
        where TEntity : class
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        entityType.Should().NotBeNull();

        var index = entityType!.GetIndexes()
            .SingleOrDefault(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

        index.Should().NotBeNull($"{typeof(TEntity).Name} should have an index on ({string.Join(", ", propertyNames)})");
        index!.IsUnique.Should().Be(unique);
    }
}
