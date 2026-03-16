using Application.Services;
using Domain.Entites;
using Domain.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class RbacAuthorizationServiceTests
{
    [Fact]
    public async Task GetPermissionsAsync_ShouldReturnRbacPermissions_WhenUserRoleAssignmentsExist()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Tenant A", Subdomain = "a", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "admin@a.com", PasswordHash = "hash", Role = "MEMBER", CreatedAt = DateTime.UtcNow };
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Owner", CreatedAt = DateTime.UtcNow };
        var permission = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.UserManage, CreatedAt = DateTime.UtcNow };

        var dbContext = CreateDbContext();
        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.Users.AddAsync(user);
        await dbContext.Roles.AddAsync(role);
        await dbContext.Permissions.AddAsync(permission);
        await dbContext.RolePermissions.AddAsync(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        await dbContext.UserRoles.AddAsync(new UserRole { TenantId = tenant.Id, UserId = user.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var sut = new RbacAuthorizationService(dbContext);

        var permissions = await sut.GetPermissionsAsync(tenant.Id, user.Id);

        permissions.Should().Contain(PermissionCodes.UserManage);
        permissions.Should().NotContain(PermissionCodes.TenantManage);
    }

    [Fact]
    public async Task HasPermissionAsync_ShouldFallbackToLegacyRoleMapping_WhenNoRbacAssignments()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Tenant A", Subdomain = "a", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "admin@a.com", PasswordHash = "hash", Role = "ADMIN", CreatedAt = DateTime.UtcNow };

        var dbContext = CreateDbContext();
        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.Users.AddAsync(user);
        await dbContext.SaveChangesAsync();

        var sut = new RbacAuthorizationService(dbContext);

        var allowed = await sut.HasPermissionAsync(tenant.Id, user.Id, PermissionCodes.TenantManage);

        allowed.Should().BeTrue();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
