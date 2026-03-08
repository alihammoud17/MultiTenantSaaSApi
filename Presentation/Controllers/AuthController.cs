using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Domain.Responses;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BCrypt.Net;
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

        public AuthController(
            ApplicationDbContext dbContext,
            IJwtService jwtService,
            ILogger<AuthController> logger)
        {
            _dbContext = dbContext;
            _jwtService = jwtService;
            _logger = logger;
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

                return Ok(new AuthResponse(
                    token,
                    tenant.Id,
                    user.Id,
                    user.Email,
                    DateTime.UtcNow.AddMinutes(60)
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

            var token = _jwtService.GenerateToken(user, user.Tenant);

            return Ok(new AuthResponse(
                token,
                user.TenantId,
                user.Id,
                user.Email,
                DateTime.UtcNow.AddMinutes(60)
            ));
        }
    }
}
