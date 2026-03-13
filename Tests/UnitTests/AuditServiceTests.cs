using FluentAssertions;
using Application.Services;
using Domain.Entites;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Tests.UnitTests;

public class AuditServiceTests
{
    [Fact]
    public async Task LogAsync_ShouldPersistAuditLog_WithTenantContextData()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dbContext = CreateDbContext();
        var tenantContext = new TestTenantContext(tenantId);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        httpContextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "TestAuth"));
        httpContextAccessor.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.10.10.10");

        var sut = new AuditService(dbContext, tenantContext, httpContextAccessor);

        await sut.LogAsync("USER_CREATED", nameof(User), "42", new { Field = "Email" });

        var persisted = await dbContext.AuditLogs.SingleAsync();

        persisted.TenantId.Should().Be(tenantId);
        persisted.UserId.Should().Be(userId);
        persisted.Action.Should().Be("USER_CREATED");
        persisted.EntityType.Should().Be(nameof(User));
        persisted.EntityId.Should().Be("42");
        persisted.IpAddress.Should().Be("10.10.10.10");
        persisted.Changes.Should().Contain("Field");
    }

    [Fact]
    public async Task GetTenantAuditLogsAsync_ShouldReturnOnlyTenantLogs_WithFiltersAndPaging()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var dbContext = CreateDbContext();

        await dbContext.AuditLogs.AddRangeAsync(
            new AuditLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), Action = "LOGIN", EntityType = nameof(User), EntityId = "u1", Timestamp = now.AddMinutes(-1), IpAddress = "1.1.1.1"
            },
            new AuditLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), Action = "LOGIN", EntityType = nameof(User), EntityId = "u2", Timestamp = now.AddMinutes(-3), IpAddress = "1.1.1.2"
            },
            new AuditLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), Action = "REGISTER", EntityType = nameof(Tenant), EntityId = "t1", Timestamp = now.AddMinutes(-5), IpAddress = "1.1.1.3"
            },
            new AuditLog
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, UserId = Guid.NewGuid(), Action = "LOGIN", EntityType = nameof(User), EntityId = "outside", Timestamp = now, IpAddress = "2.2.2.2"
            });
        await dbContext.SaveChangesAsync();

        var sut = new AuditService(dbContext, new TestTenantContext(tenantId), new HttpContextAccessor());

        var logs = await sut.GetTenantAuditLogsAsync(page: 1, pageSize: 1, action: "LOGIN", fromUtc: now.AddMinutes(-4), toUtc: now);

        logs.Should().HaveCount(1);
        logs.Single().TenantId.Should().Be(tenantId);
        logs.Single().Action.Should().Be("LOGIN");
        logs.Single().EntityId.Should().Be("u1");
    }

    [Fact]
    public async Task LogAsync_ShouldUseGuidEmptyAndUnknownIp_WhenContextHasNoUserOrIp()
    {
        var tenantId = Guid.NewGuid();
        var dbContext = CreateDbContext();
        var tenantContext = new TestTenantContext(tenantId);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var sut = new AuditService(dbContext, tenantContext, httpContextAccessor);

        await sut.LogAsync("TENANT_CHECKED", nameof(Tenant), "tenant-1");

        var persisted = await dbContext.AuditLogs.SingleAsync();
        persisted.TenantId.Should().Be(tenantId);
        persisted.UserId.Should().Be(Guid.Empty);
        persisted.IpAddress.Should().Be("unknown");
        persisted.Changes.Should().BeNull();
    }

    [Fact]
    public async Task GetTenantAuditLogsAsync_ShouldClampInvalidPagingValues()
    {
        var tenantId = Guid.NewGuid();
        var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;

        await dbContext.AuditLogs.AddRangeAsync(
            Enumerable.Range(1, 5).Select(i => new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = Guid.NewGuid(),
                Action = "EVENT",
                EntityType = nameof(User),
                EntityId = i.ToString(),
                Timestamp = now.AddMinutes(-i),
                IpAddress = "127.0.0.1"
            }));
        await dbContext.SaveChangesAsync();

        var sut = new AuditService(dbContext, new TestTenantContext(tenantId), new HttpContextAccessor());

        var logs = await sut.GetTenantAuditLogsAsync(page: 0, pageSize: 0);

        logs.Should().HaveCount(1);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; private set; } = tenantId;

        public void SetTenantId(Guid tenantId)
        {
            TenantId = tenantId;
        }
    }
}
