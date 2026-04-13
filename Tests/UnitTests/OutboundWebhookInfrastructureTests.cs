using Application.Services;
using Domain.DTOs;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class OutboundWebhookInfrastructureTests
{
    [Fact]
    public void ComputeSignature_ShouldBeDeterministic()
    {
        var signature = OutboundWebhookSigner.ComputeSignature("secret-123", "2026-04-13T00:00:00.0000000+00:00", "delivery-1", "{\"hello\":\"world\"}");

        signature.Should().Be("sha256=9674efbad4d886df4a63b72b26b33e98a73e25d90b08192027d05529ba749e57");
    }

    [Fact]
    public async Task PublishAsync_ShouldCreateEventAndDedupeBySourceEventKey()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var tenantId = Guid.NewGuid();
        dbContext.Tenants.Add(new Tenant { Id = tenantId, Name = "Tenant", Subdomain = "tenant", Status = TenantStatus.Active });
        dbContext.TenantWebhookEndpoints.Add(new TenantWebhookEndpoint
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "main",
            CallbackUrl = "https://example.com/webhooks",
            SigningSecret = "secret",
            SubscribedEventTypes = "*",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var publisher = new OutboundWebhookPublisher(dbContext);

        var request = new OutboundWebhookPublishRequest(
            tenantId,
            "tenant.subscription.updated",
            new { subscriptionId = Guid.NewGuid(), status = "Active" },
            "corr-1",
            DateTime.UtcNow,
            "source-evt-1");

        await publisher.PublishAsync(request);
        await publisher.PublishAsync(request);

        dbContext.OutboundWebhookEvents.Count().Should().Be(1);
        dbContext.OutboundWebhookDeliveries.Count().Should().Be(1);
    }
}
