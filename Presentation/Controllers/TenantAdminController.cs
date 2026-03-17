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
    public class TenantAdminController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly IAuditService _auditService;

        public TenantAdminController(
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
                    t.UpdatedAt,
                    UserCount = t.Users.Count,
                    PlanId = t.Subscription != null ? t.Subscription.PlanId : null,
                    SubscriptionStatus = t.Subscription != null ? t.Subscription.Status.ToString() : null
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
                .OrderBy(u => u.CreatedAt)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.Role,
                    u.CreatedAt,
                    u.LastLoginAt
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpPost("users")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> AddTenantUser([FromBody] InviteTenantUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { error = "Email is required" });

            if (string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Password is required" });

            if (string.IsNullOrWhiteSpace(request.Role))
                return BadRequest(new { error = "Role is required" });

            var tenantId = _tenantContext.TenantId;
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var normalizedRole = request.Role.Trim().ToUpperInvariant();

            var emailExists = await _dbContext.Users
                .AnyAsync(u => u.TenantId == tenantId && u.Email == normalizedEmail);

            if (emailExists)
                return Conflict(new { error = "Email already exists for tenant" });

            var user = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = normalizedEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = normalizedRole,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddAsync(user);
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("TENANT_USER_ADDED", nameof(User), user.Id.ToString(), new
            {
                user.Email,
                user.Role
            });

            return CreatedAtAction(nameof(ListTenantUsers), new
            {
                id = user.Id
            }, new
            {
                user.Id,
                user.Email,
                user.Role,
                user.CreatedAt
            });
        }

        [HttpDelete("users/{userId:guid}")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> RemoveTenantUser(Guid userId)
        {
            var tenantId = _tenantContext.TenantId;
            var requesterId = GetCurrentUserId();

            if (requesterId == userId)
                return BadRequest(new { error = "Cannot remove the current user" });

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId);

            if (user == null)
                return NotFound(new { error = "User not found" });

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("TENANT_USER_REMOVED", nameof(User), user.Id.ToString(), new
            {
                user.Email,
                user.Role
            });

            return NoContent();
        }

        [HttpPatch("users/{userId:guid}/role")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> ChangeTenantUserRole(Guid userId, [FromBody] ChangeTenantUserRoleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Role))
                return BadRequest(new { error = "Role is required" });

            var tenantId = _tenantContext.TenantId;

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId);

            if (user == null)
                return NotFound(new { error = "User not found" });

            var previousRole = user.Role;
            user.Role = request.Role.Trim().ToUpperInvariant();

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("TENANT_USER_ROLE_CHANGED", nameof(User), user.Id.ToString(), new
            {
                OldRole = previousRole,
                NewRole = user.Role,
                user.Email
            });

            return Ok(new
            {
                user.Id,
                user.Email,
                user.Role
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

        private Guid GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("sub")?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
        }
    }
}
