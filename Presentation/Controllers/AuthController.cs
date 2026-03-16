using Domain.Authorization;
using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Domain.Responses;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IJwtService _jwtService;
        private readonly ILogger<AuthController> _logger;
        private readonly IAuditService _auditService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ITenantContext _tenantContext;

        public AuthController(
            ApplicationDbContext dbContext,
            IJwtService jwtService,
            ILogger<AuthController> logger,
            IAuditService auditService,
            IRefreshTokenService refreshTokenService,
            ITenantContext tenantContext)
        {
            _dbContext = dbContext;
            _jwtService = jwtService;
            _logger = logger;
            _auditService = auditService;
            _refreshTokenService = refreshTokenService;
            _tenantContext = tenantContext;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterTenantRequest request)
        {
            if (await _dbContext.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain))
                return BadRequest(new { error = "Subdomain already taken" });

            if (await _dbContext.Users.AnyAsync(u => u.Email == request.AdminEmail))
                return BadRequest(new { error = "Email already registered" });

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                var tenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = request.CompanyName,
                    Subdomain = request.Subdomain,
                    Status = TenantStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _dbContext.Tenants.AddAsync(tenant);

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    Email = request.AdminEmail,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
                    Role = "ADMIN",
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.Users.AddAsync(user);

                var subscription = new Subscription
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    PlanId = "plan-free",
                    Status = SubscriptionStatus.Active,
                    CurrentPeriodStart = DateTime.UtcNow,
                    CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.Subscriptions.AddAsync(subscription);

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                var token = _jwtService.GenerateToken(user, tenant);

                _logger.LogInformation("New tenant registered: {TenantId}", tenant.Id);
                await _auditService.LogAsync("TENANT_REGISTERED", nameof(Tenant), tenant.Id.ToString(), new
                {
                    tenant.Name,
                    tenant.Subdomain,
                    AdminUserId = user.Id
                });

                var refreshTokenResult = await _refreshTokenService.IssueTokenAsync(
                    tenant.Id,
                    user.Id,
                    DateTime.UtcNow.AddDays(7),
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                return Ok(new AuthResponse(
                    token,
                    refreshTokenResult.Token,
                    tenant.Id,
                    user.Id,
                    user.Email,
                    DateTime.UtcNow.AddMinutes(60),
                    refreshTokenResult.ExpiresAt
                ));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to register tenant");
                return StatusCode(500, new { error = "Registration failed" });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var user = await _dbContext.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == request.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized(new { error = "Invalid credentials" });

            if (user.Tenant.Status != TenantStatus.Active)
                return StatusCode(403, new { error = "Tenant account is suspended" });

            user.LastLoginAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("USER_LOGGED_IN", nameof(User), user.Id.ToString(), new
            {
                user.Email,
                user.Role
            });

            var token = _jwtService.GenerateToken(user, user.Tenant);
            var refreshTokenResult = await _refreshTokenService.IssueTokenAsync(
                user.TenantId,
                user.Id,
                DateTime.UtcNow.AddDays(7),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new AuthResponse(
                token,
                refreshTokenResult.Token,
                user.TenantId,
                user.Id,
                user.Email,
                DateTime.UtcNow.AddMinutes(60),
                refreshTokenResult.ExpiresAt
            ));
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });

            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return BadRequest(new { error = "RefreshToken is required" });

            _tenantContext.SetTenantId(request.TenantId);

            var activeToken = await _refreshTokenService.GetActiveTokenAsync(request.TenantId, request.RefreshToken, cancellationToken);
            if (activeToken == null)
                return Unauthorized(new { error = "Invalid or expired refresh token" });

            var user = await _dbContext.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == activeToken.UserId && u.TenantId == request.TenantId, cancellationToken);

            if (user == null || user.Tenant.Status != TenantStatus.Active)
                return Unauthorized(new { error = "Invalid refresh token context" });

            var rotated = await _refreshTokenService.RotateTokenAsync(
                request.TenantId,
                request.RefreshToken,
                DateTime.UtcNow.AddDays(7),
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            if (rotated == null)
                return Unauthorized(new { error = "Invalid or expired refresh token" });

            var accessToken = _jwtService.GenerateToken(user, user.Tenant);

            await _auditService.LogAsync("USER_TOKEN_REFRESHED", nameof(User), user.Id.ToString(), new
            {
                user.Email
            });

            return Ok(new AuthResponse(
                accessToken,
                rotated.Token,
                user.TenantId,
                user.Id,
                user.Email,
                DateTime.UtcNow.AddMinutes(60),
                rotated.ExpiresAt
            ));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RevokeRefreshTokenRequest request, CancellationToken cancellationToken)
        {
            return await RevokeRefreshTokenInternal(request, "LOGOUT", "USER_LOGGED_OUT", cancellationToken);
        }

        [HttpPost("revoke")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> Revoke([FromBody] RevokeRefreshTokenRequest request, CancellationToken cancellationToken)
        {
            return await RevokeRefreshTokenInternal(request, request.Reason, "USER_TOKEN_REVOKED", cancellationToken);
        }

        private async Task<IActionResult> RevokeRefreshTokenInternal(
            RevokeRefreshTokenRequest request,
            string? defaultReason,
            string auditAction,
            CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });

            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return BadRequest(new { error = "RefreshToken is required" });

            _tenantContext.SetTenantId(request.TenantId);

            var activeToken = await _refreshTokenService.GetActiveTokenAsync(request.TenantId, request.RefreshToken, cancellationToken);
            if (activeToken == null)
                return Unauthorized(new { error = "Invalid or expired refresh token" });

            var revoked = await _refreshTokenService.RevokeTokenAsync(
                request.TenantId,
                request.RefreshToken,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                string.IsNullOrWhiteSpace(request.Reason) ? defaultReason : request.Reason,
                cancellationToken);

            if (!revoked)
                return Unauthorized(new { error = "Invalid or expired refresh token" });

            await _auditService.LogAsync(auditAction, nameof(User), activeToken.UserId.ToString(), new
            {
                TenantId = request.TenantId,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? defaultReason : request.Reason
            });

            return Ok(new { message = "Refresh token revoked" });
        }
    }
}
