using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class RbacAuthorizationService : IRbacAuthorizationService
    {
        private static readonly IReadOnlyDictionary<string, string[]> LegacyRolePermissions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADMIN"] = PermissionCodes.All,
            ["MEMBER"] = [PermissionCodes.TenantRead, PermissionCodes.PlanRead]
        };

        private readonly ApplicationDbContext _dbContext;

        public RbacAuthorizationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            var permissions = await GetPermissionsAsync(tenantId, userId, cancellationToken);
            return permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
        {
            var rbacPermissions = await _dbContext.UserRoles
                .Where(ur => ur.TenantId == tenantId && ur.UserId == userId)
                .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
                .Distinct()
                .ToListAsync(cancellationToken);

            if (rbacPermissions.Count > 0)
            {
                return rbacPermissions;
            }

            var legacyRole = await _dbContext.Users
                .Where(u => u.TenantId == tenantId && u.Id == userId)
                .Select(u => u.Role)
                .SingleOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(legacyRole) && LegacyRolePermissions.TryGetValue(legacyRole, out var mapped))
            {
                return mapped;
            }

            return [];
        }
    }
}
