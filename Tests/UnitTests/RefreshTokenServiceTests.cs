using Application.Services;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class RefreshTokenServiceTests
{
    [Fact]
    public async Task IssueTokenAsync_ShouldPersistHashedToken_ForTenantUser()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Tenant A", Subdomain = "a", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "admin@a.com", PasswordHash = "hash", CreatedAt = DateTime.UtcNow };

        var dbContext = CreateDbContext();
        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.Users.AddAsync(user);
        await dbContext.SaveChangesAsync();

        var sut = new RefreshTokenService(dbContext);

        var issued = await sut.IssueTokenAsync(tenant.Id, user.Id, DateTime.UtcNow.AddDays(7), "127.0.0.1");

        issued.Token.Should().NotBeNullOrWhiteSpace();

        var stored = await dbContext.RefreshTokens.SingleAsync();
        stored.TenantId.Should().Be(tenant.Id);
        stored.UserId.Should().Be(user.Id);
        stored.TokenHash.Should().NotBe(issued.Token);
        stored.CreatedByIp.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task GetActiveTokenAsync_ShouldNotReturnToken_FromAnotherTenant()
    {
        var tenantA = new Tenant { Id = Guid.NewGuid(), Name = "Tenant A", Subdomain = "a", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var tenantB = new Tenant { Id = Guid.NewGuid(), Name = "Tenant B", Subdomain = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var userA = new User { Id = Guid.NewGuid(), TenantId = tenantA.Id, Email = "admin@a.com", PasswordHash = "hash", CreatedAt = DateTime.UtcNow };

        var dbContext = CreateDbContext();
        await dbContext.Tenants.AddRangeAsync(tenantA, tenantB);
        await dbContext.Users.AddAsync(userA);
        await dbContext.SaveChangesAsync();

        var sut = new RefreshTokenService(dbContext);
        var issued = await sut.IssueTokenAsync(tenantA.Id, userA.Id, DateTime.UtcNow.AddDays(7));

        var fromWrongTenant = await sut.GetActiveTokenAsync(tenantB.Id, issued.Token);
        var fromCorrectTenant = await sut.GetActiveTokenAsync(tenantA.Id, issued.Token);

        fromWrongTenant.Should().BeNull();
        fromCorrectTenant.Should().NotBeNull();
    }

    [Fact]
    public async Task RotateTokenAsync_ShouldRevokeCurrentToken_AndIssueReplacement()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Tenant A", Subdomain = "a", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "admin@a.com", PasswordHash = "hash", CreatedAt = DateTime.UtcNow };

        var dbContext = CreateDbContext();
        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.Users.AddAsync(user);
        await dbContext.SaveChangesAsync();

        var sut = new RefreshTokenService(dbContext);
        var original = await sut.IssueTokenAsync(tenant.Id, user.Id, DateTime.UtcNow.AddDays(7));

        var rotated = await sut.RotateTokenAsync(tenant.Id, original.Token, DateTime.UtcNow.AddDays(7), "10.1.1.1");

        rotated.Should().NotBeNull();
        rotated!.Token.Should().NotBe(original.Token);

        var storedTokens = await dbContext.RefreshTokens.OrderBy(x => x.CreatedAt).ToListAsync();
        storedTokens.Should().HaveCount(2);
        storedTokens[0].RevokedAt.Should().NotBeNull();
        storedTokens[0].ReplacedByTokenId.Should().Be(storedTokens[1].Id);
        storedTokens[0].RevokedByIp.Should().Be("10.1.1.1");
        storedTokens[1].RevokedAt.Should().BeNull();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
