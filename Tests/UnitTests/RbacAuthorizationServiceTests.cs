using Application.Services;
using Domain.Authorization;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class RbacAuthorizationServiceTests
{
    [Fact]
    public async Task HasPermissionAsync_ShouldReturnTrue_WhenRoleGrantsPermissionWithinTenant()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        var dbContext = CreateDbContext();
        await SeedTenantAndUser(dbContext, tenantId, userId, role: "MEMBER");

        await dbContext.Roles.AddAsync(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = "Manager",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await dbContext.Permissions.AddAsync(new Permission
        {
            Id = permissionId,
            Name = RbacPermissions.UsersManage,
            Description = "Manage users"
        });

        await dbContext.UserRoles.AddAsync(new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = DateTime.UtcNow
        });

        await dbContext.RolePermissions.AddAsync(new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId,
            GrantedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var sut = new RbacAuthorizationService(dbContext);

        var result = await sut.HasPermissionAsync(tenantId, userId, RbacPermissions.UsersManage);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_ShouldReturnFalse_WhenPermissionExistsInAnotherTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        var dbContext = CreateDbContext();
        await SeedTenantAndUser(dbContext, tenantA, userA, role: "MEMBER");
        await SeedTenantAndUser(dbContext, tenantB, Guid.NewGuid(), role: "MEMBER", email: "other@tenantb.com", subdomain: "tenant-b");

        await dbContext.Roles.AddAsync(new Role
        {
            Id = roleB,
            TenantId = tenantB,
            Name = "Manager",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await dbContext.Permissions.AddAsync(new Permission
        {
            Id = permissionId,
            Name = RbacPermissions.AuditLogsRead
        });

        await dbContext.UserRoles.AddAsync(new UserRole
        {
            TenantId = tenantB,
            UserId = userA,
            RoleId = roleB,
            AssignedAt = DateTime.UtcNow
        });

        await dbContext.RolePermissions.AddAsync(new RolePermission
        {
            RoleId = roleB,
            PermissionId = permissionId,
            GrantedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var sut = new RbacAuthorizationService(dbContext);

        var result = await sut.HasPermissionAsync(tenantA, userA, RbacPermissions.AuditLogsRead);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_ShouldReturnTrue_ForLegacyAdminWithoutRoleMappings()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dbContext = CreateDbContext();

        await SeedTenantAndUser(dbContext, tenantId, userId, role: "ADMIN");

        var sut = new RbacAuthorizationService(dbContext);

        var result = await sut.HasPermissionAsync(tenantId, userId, RbacPermissions.TenantsManage);

        result.Should().BeTrue();
    }

    private static async Task SeedTenantAndUser(ApplicationDbContext dbContext, Guid tenantId, Guid userId, string role, string? email = null, string? subdomain = null)
    {
        var suffix = subdomain ?? tenantId.ToString("N")[..8];

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = $"Tenant-{suffix}",
            Subdomain = subdomain ?? $"tenant-{suffix}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = email ?? $"user-{suffix}@example.com",
            PasswordHash = "hash",
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.Users.AddAsync(user);
        await dbContext.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
