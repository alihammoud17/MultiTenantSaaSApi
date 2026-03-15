using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Domain.Responses;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

        public AuthController(
            ApplicationDbContext dbContext,
            IJwtService jwtService,
            ILogger<AuthController> logger,
            IAuditService auditService,
            IRefreshTokenService refreshTokenService)
        {
            _dbContext = dbContext;
            _jwtService = jwtService;
            _logger = logger;
            _auditService = auditService;
            _refreshTokenService = refreshTokenService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterTenantRequest request)
        {
            // Validate subdomain is available
            if (await _dbContext.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain))
                return BadRequest(new { error = "Subdomain already taken" });

            // Validate email
            if (await _dbContext.Users.AnyAsync(u => u.Email == request.AdminEmail))
                return BadRequest(new { error = "Email already registered" });

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // Create tenant
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

                // Create admin user
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

                // Create free subscription
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

                // Generate JWT
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

            // Update last login
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
            {
                return BadRequest(new { error = "TenantId is required" });
            }

            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return BadRequest(new { error = "RefreshToken is required" });
            }

            var rotated = await _refreshTokenService.RotateTokenAsync(
                request.TenantId,
                request.RefreshToken,
                DateTime.UtcNow.AddDays(7),
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            if (rotated == null)
            {
                return Unauthorized(new { error = "Invalid or expired refresh token" });
            }

            var refreshTokenRecord = await _dbContext.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == rotated.RefreshTokenId && t.TenantId == request.TenantId, cancellationToken);

            if (refreshTokenRecord == null)
            {
                return Unauthorized(new { error = "Invalid refresh token context" });
            }

            var user = await _dbContext.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == refreshTokenRecord.UserId && u.TenantId == request.TenantId, cancellationToken);

            if (user == null || user.Tenant.Status != TenantStatus.Active)
            {
                return Unauthorized(new { error = "Invalid refresh token context" });
            }

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
            if (request.TenantId == Guid.Empty)
            {
                return BadRequest(new { error = "TenantId is required" });
            }

            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return BadRequest(new { error = "RefreshToken is required" });
            }

            var revoked = await _refreshTokenService.RevokeTokenAsync(
                request.TenantId,
                request.RefreshToken,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                string.IsNullOrWhiteSpace(request.Reason) ? "LOGOUT" : request.Reason,
                cancellationToken);

            if (!revoked)
            {
                return Unauthorized(new { error = "Invalid or expired refresh token" });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            await _auditService.LogAsync("USER_LOGGED_OUT", nameof(User), userId ?? Guid.Empty.ToString(), new
            {
                TenantId = request.TenantId
            });

            return Ok(new { message = "Refresh token revoked" });
        }

        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke([FromBody] RevokeRefreshTokenRequest request, CancellationToken cancellationToken)
        {
            return await Logout(request, cancellationToken);
        }

    }
}
