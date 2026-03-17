using Domain.Authorization;
using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Controllers
{
    [Route("api/admin/tenant")]
    [ApiController]
    [Authorize]
    public class AdminTenantManagementController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly IAuditService _auditService;

        public AdminTenantManagementController(
            ApplicationDbContext dbContext,
            ITenantContext tenantContext,
            IAuditService auditService)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _auditService = auditService;
        }

        [HttpGet]
        [Authorize(Policy = RbacPolicyNames.TenantsRead)]
        public async Task<IActionResult> GetTenantDetails()
        {
            var tenantId = _tenantContext.TenantId;
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Subdomain,
                    Status = t.Status.ToString(),
                    t.CreatedAt,
                    t.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (tenant == null)
                return NotFound(new { error = "Tenant not found" });

            return Ok(tenant);
        }

        [HttpGet("users")]
        [Authorize(Policy = RbacPolicyNames.UsersRead)]
        public async Task<IActionResult> ListTenantUsers()
        {
            var tenantId = _tenantContext.TenantId;

            var users = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId)
                .OrderBy(u => u.Email)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.Role,
                    u.CreatedAt,
                    u.LastLoginAt,
                    UserRoleNames = u.UserRoles.Select(ur => ur.Role.Name)
                })
                .ToListAsync();

            return Ok(users.Select(u => new
            {
                u.Id,
                u.Email,
                u.Role,
                u.CreatedAt,
                u.LastLoginAt,
                RbacRoles = u.UserRoleNames.OrderBy(x => x)
            }));
        }

        [HttpPost("users")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> InviteOrAddUser([FromBody] InviteTenantUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { error = "Email is required" });

            if (string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Password is required" });

            var tenantId = _tenantContext.TenantId;

            var exists = await _dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Email == request.Email);
            if (exists)
                return BadRequest(new { error = "User already exists for tenant" });

            var user = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = string.IsNullOrWhiteSpace(request.Role) ? "MEMBER" : request.Role.Trim().ToUpperInvariant(),
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddAsync(user);

            if (!string.IsNullOrWhiteSpace(request.RbacRoleName))
            {
                var role = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == request.RbacRoleName);

                if (role == null)
                    return BadRequest(new { error = "RBAC role not found for tenant" });

                await _dbContext.UserRoles.AddAsync(new UserRole
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    RoleId = role.Id,
                    AssignedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("TENANT_USER_ADDED", nameof(User), user.Id.ToString(), new
            {
                user.Email,
                user.Role,
                request.RbacRoleName
            });

            return Created($"/api/admin/tenant/users/{user.Id}", new
            {
                user.Id,
                user.Email,
                user.Role
            });
        }

        [HttpDelete("users/{userId:guid}")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> RemoveUser(Guid userId)
        {
            var tenantId = _tenantContext.TenantId;
            var actorUserId = GetActorUserId();
            if (actorUserId == userId)
                return BadRequest(new { error = "Cannot remove current authenticated user" });

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId);

            if (user == null)
                return NotFound(new { error = "User not found" });

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("TENANT_USER_REMOVED", nameof(User), user.Id.ToString(), new
            {
                user.Email
            });

            return NoContent();
        }

        [HttpPut("users/{userId:guid}/role")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> AssignOrChangeRole(Guid userId, [FromBody] ChangeTenantUserRoleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Role))
                return BadRequest(new { error = "Role is required" });

            var tenantId = _tenantContext.TenantId;
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId);

            if (user == null)
                return NotFound(new { error = "User not found" });

            user.Role = request.Role.Trim().ToUpperInvariant();

            var existingAssignments = _dbContext.UserRoles
                .Where(ur => ur.TenantId == tenantId && ur.UserId == userId);
            _dbContext.UserRoles.RemoveRange(existingAssignments);

            if (!string.IsNullOrWhiteSpace(request.RbacRoleName))
            {
                var role = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == request.RbacRoleName);

                if (role == null)
                    return BadRequest(new { error = "RBAC role not found for tenant" });

                await _dbContext.UserRoles.AddAsync(new UserRole
                {
                    TenantId = tenantId,
                    UserId = userId,
                    RoleId = role.Id,
                    AssignedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("TENANT_USER_ROLE_CHANGED", nameof(User), user.Id.ToString(), new
            {
                user.Email,
                user.Role,
                request.RbacRoleName
            });

            return Ok(new
            {
                user.Id,
                user.Email,
                user.Role,
                request.RbacRoleName
            });
        }

        [HttpGet("audit-logs")]
        [Authorize(Policy = RbacPolicyNames.AuditLogsRead)]
        public async Task<IActionResult> GetTenantAuditLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? action = null,
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null)
        {
            var logs = await _auditService.GetTenantAuditLogsAsync(page, pageSize, action, fromUtc, toUtc);
            return Ok(logs);
        }

        private Guid GetActorUserId()
        {
            var subject = User.FindFirst("sub")?.Value;
            return Guid.TryParse(subject, out var userId) ? userId : Guid.Empty;
        }
    }
}
