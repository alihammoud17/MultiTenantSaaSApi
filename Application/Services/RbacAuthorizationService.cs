using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class RbacAuthorizationService : IRbacAuthorizationService
    {
        private readonly ApplicationDbContext _dbContext;

        public RbacAuthorizationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, string permission, CancellationToken cancellationToken = default)
        {
            var isLegacyAdmin = await _dbContext.Users
                .AnyAsync(u => u.TenantId == tenantId && u.Id == userId && u.Role == "ADMIN", cancellationToken);

            if (isLegacyAdmin)
                return true;

            return await _dbContext.UserRoles
                .Join(_dbContext.Roles,
                    ur => ur.RoleId,
                    role => role.Id,
                    (ur, role) => new { ur, role })
                .Join(_dbContext.RolePermissions,
                    x => x.role.Id,
                    rp => rp.RoleId,
                    (x, rp) => new { x.ur, x.role, rp })
                .Join(_dbContext.Permissions,
                    x => x.rp.PermissionId,
                    permissionEntity => permissionEntity.Id,
                    (x, permissionEntity) => new { x.ur, x.role, permissionEntity })
                .AnyAsync(x =>
                    x.ur.TenantId == tenantId &&
                    x.ur.UserId == userId &&
                    x.role.TenantId == tenantId &&
                    x.permissionEntity.Name == permission,
                    cancellationToken);
        }
    }
}
