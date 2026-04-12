using Domain.Authorization;
using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Domain.Responses;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Presentation.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IJwtService _jwtService;
        private readonly ILogger<AuthController> _logger;
        private readonly IAuditService _auditService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ITenantContext _tenantContext;
        private readonly IIdentityLifecycleService _identityLifecycleService;
        private readonly IMfaService _mfaService;

        public AuthController(
            ApplicationDbContext dbContext,
            IJwtService jwtService,
            ILogger<AuthController> logger,
            IAuditService auditService,
            IRefreshTokenService refreshTokenService,
            ITenantContext tenantContext,
            IIdentityLifecycleService identityLifecycleService,
            IMfaService mfaService)
        {
            _dbContext = dbContext;
            _jwtService = jwtService;
            _logger = logger;
            _auditService = auditService;
            _refreshTokenService = refreshTokenService;
            _tenantContext = tenantContext;
            _identityLifecycleService = identityLifecycleService;
            _mfaService = mfaService;
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
                    EmailVerifiedAt = DateTime.UtcNow,
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
                _tenantContext.SetTenantId(tenant.Id);
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

            if (!user.EmailVerifiedAt.HasValue)
                return Unauthorized(new { error = "Email is not verified" });

            if (user.Tenant.Status != TenantStatus.Active)
                return StatusCode(403, new { error = "Tenant account is suspended" });

            if (user.MfaEnabled)
                return Unauthorized(new { error = "MFA challenge required", requiresMfa = true });

            user.LastLoginAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _tenantContext.SetTenantId(user.TenantId);
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
            var stepUp = await ValidateStepUpOrForbidAsync("admin_sensitive", cancellationToken);
            if (stepUp is not null)
                return stepUp;
            return await RevokeRefreshTokenInternal(request, request.Reason, "USER_TOKEN_REVOKED", cancellationToken);
        }

        [HttpGet("sessions")]
        [Authorize]
        public async Task<IActionResult> GetSessions([FromQuery] string? currentRefreshToken, CancellationToken cancellationToken)
        {
            var tenantId = ResolveCurrentTenantId();
            var userId = ResolveCurrentUserId();
            if (tenantId == Guid.Empty || userId == Guid.Empty)
            {
                return Unauthorized(new { error = "Invalid authenticated context" });
            }

            _tenantContext.SetTenantId(tenantId);
            var sessions = await _refreshTokenService.GetActiveSessionsAsync(tenantId, userId, currentRefreshToken, cancellationToken);
            return Ok(sessions);
        }

        [HttpPost("sessions/revoke-all")]
        [Authorize]
        public async Task<IActionResult> RevokeAllSessions([FromBody] RevokeAllSessionsRequest request, CancellationToken cancellationToken)
        {
            var stepUp = await ValidateStepUpOrForbidAsync("admin_sensitive", cancellationToken);
            if (stepUp is not null)
                return stepUp;

            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });

            var actorTenantId = ResolveCurrentTenantId();
            var actorUserId = ResolveCurrentUserId();
            if (actorTenantId == Guid.Empty || actorTenantId != request.TenantId || actorUserId == Guid.Empty)
                return Unauthorized(new { error = "Invalid authenticated context" });

            var effectiveUserId = request.UserId ?? actorUserId;
            if (effectiveUserId != actorUserId && !string.Equals(User.FindFirst("role")?.Value, "ADMIN", StringComparison.OrdinalIgnoreCase))
                return Forbid();

            _tenantContext.SetTenantId(request.TenantId);
            var revokedCount = await _refreshTokenService.RevokeAllActiveTokensAsync(
                request.TenantId,
                effectiveUserId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                string.IsNullOrWhiteSpace(request.Reason) ? "REVOKE_ALL" : request.Reason,
                cancellationToken: cancellationToken);

            await _auditService.LogAsync("USER_SESSIONS_REVOKED_ALL", nameof(User), effectiveUserId.ToString(), new
            {
                TenantId = request.TenantId,
                RevokedCount = revokedCount,
                ActorUserId = actorUserId
            });

            return Ok(new
            {
                revokedCount,
                userId = effectiveUserId
            });
        }

        [HttpPost("invites")]
        [Authorize(Policy = RbacPolicyNames.UsersManage)]
        public async Task<IActionResult> CreateInvite([FromBody] CreateInviteRequest request, CancellationToken cancellationToken)
        {
            var stepUp = await ValidateStepUpOrForbidAsync("admin_sensitive", cancellationToken);
            if (stepUp is not null)
                return stepUp;

            var tenantId = ResolveCurrentTenantId();
            var actorUserId = ResolveCurrentUserId();
            if (tenantId == Guid.Empty || actorUserId == Guid.Empty)
                return Unauthorized(new { error = "Invalid authenticated context" });

            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { error = "Email is required" });

            _tenantContext.SetTenantId(tenantId);

            try
            {
                var result = await _identityLifecycleService.CreateInviteAsync(
                    tenantId,
                    actorUserId,
                    request.Email,
                    request.Role,
                    request.RbacRoleName,
                    request.ExpiresInHours,
                    cancellationToken);

                await _auditService.LogAsync("USER_INVITE_CREATED", nameof(User), actorUserId.ToString(), new
                {
                    request.Email,
                    request.Role,
                    request.RbacRoleName,
                    result.ExpiresAt
                });

                return Ok(result);
            }
            catch (InvalidOperationException)
            {
                return BadRequest(new { error = "User already exists for tenant" });
            }
        }

        [HttpPost("invites/accept")]
        public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request, CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });
            if (string.IsNullOrWhiteSpace(request.InviteToken))
                return BadRequest(new { error = "InviteToken is required" });
            if (string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Password is required" });

            _tenantContext.SetTenantId(request.TenantId);
            var userId = await _identityLifecycleService.AcceptInviteAsync(request.TenantId, request.InviteToken, request.Password, cancellationToken);
            if (!userId.HasValue)
                return BadRequest(new { error = "Invalid or expired invite token" });

            await _auditService.LogAsync("USER_INVITE_ACCEPTED", nameof(User), userId.Value.ToString(), new
            {
                request.TenantId
            });

            return Ok(new { message = "Invite accepted. Verify email before login." });
        }

        [HttpPost("verification/request")]
        public async Task<IActionResult> RequestVerification([FromBody] RequestVerificationRequest request, CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { error = "Email is required" });

            _tenantContext.SetTenantId(request.TenantId);
            await _identityLifecycleService.RequestVerificationAsync(request.TenantId, request.Email, cancellationToken);
            return Ok(new { message = "If account exists, verification was requested." });
        }

        [HttpPost("verification/complete")]
        public async Task<IActionResult> CompleteVerification([FromBody] CompleteVerificationRequest request, CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });
            if (string.IsNullOrWhiteSpace(request.VerificationToken))
                return BadRequest(new { error = "VerificationToken is required" });

            _tenantContext.SetTenantId(request.TenantId);
            var verified = await _identityLifecycleService.CompleteVerificationAsync(request.TenantId, request.VerificationToken, cancellationToken);
            if (!verified)
                return BadRequest(new { error = "Invalid or expired verification token" });

            return Ok(new { message = "Email verified." });
        }

        [HttpPost("password-reset/request")]
        public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest request, CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { error = "Email is required" });

            _tenantContext.SetTenantId(request.TenantId);
            await _identityLifecycleService.RequestPasswordResetAsync(
                request.TenantId,
                request.Email,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            return Ok(new { message = "If account exists, password reset was requested." });
        }

        [HttpPost("password-reset/complete")]
        public async Task<IActionResult> CompletePasswordReset([FromBody] CompletePasswordResetRequest request, CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });
            if (string.IsNullOrWhiteSpace(request.ResetToken))
                return BadRequest(new { error = "ResetToken is required" });
            if (string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest(new { error = "NewPassword is required" });

            _tenantContext.SetTenantId(request.TenantId);
            var updated = await _identityLifecycleService.CompletePasswordResetAsync(
                request.TenantId,
                request.ResetToken,
                request.NewPassword,
                cancellationToken);

            if (!updated)
                return BadRequest(new { error = "Invalid or expired reset token" });

            return Ok(new { message = "Password reset successful." });
        }

        [HttpPost("mfa/enroll/initiate")]
        [Authorize]
        public async Task<IActionResult> InitiateMfaEnrollment(CancellationToken cancellationToken)
        {
            var tenantId = ResolveCurrentTenantId();
            var userId = ResolveCurrentUserId();
            if (tenantId == Guid.Empty || userId == Guid.Empty)
                return Unauthorized(new { error = "Invalid authenticated context" });

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null)
                return Unauthorized(new { error = "Invalid authenticated context" });
            if (user.MfaEnabled)
                return BadRequest(new { error = "MFA is already enabled" });

            var (secret, provisioningUri) = _mfaService.GenerateEnrollmentSecret("MultiTenantSaaSApi", user.Email);
            var enrollmentToken = _mfaService.GenerateOpaqueToken();

            _dbContext.UserMfaEnrollmentChallenges.Add(new UserMfaEnrollmentChallenge
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                EnrollmentTokenHash = _mfaService.HashToken(enrollmentToken),
                Secret = secret,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                enrollmentToken,
                secret,
                provisioningUri,
                expiresAt = DateTime.UtcNow.AddMinutes(10)
            });
        }

        [HttpPost("mfa/enroll/verify")]
        [Authorize]
        public async Task<IActionResult> CompleteMfaEnrollment([FromBody] CompleteMfaEnrollmentRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.EnrollmentToken))
                return BadRequest(new { error = "EnrollmentToken is required" });
            if (string.IsNullOrWhiteSpace(request.Code))
                return BadRequest(new { error = "Code is required" });

            var tenantId = ResolveCurrentTenantId();
            var userId = ResolveCurrentUserId();
            if (tenantId == Guid.Empty || userId == Guid.Empty)
                return Unauthorized(new { error = "Invalid authenticated context" });

            var tokenHash = _mfaService.HashToken(request.EnrollmentToken.Trim());
            var challenge = await _dbContext.UserMfaEnrollmentChallenges
                .FirstOrDefaultAsync(c =>
                    c.TenantId == tenantId &&
                    c.UserId == userId &&
                    c.EnrollmentTokenHash == tokenHash &&
                    c.ConsumedAt == null, cancellationToken);

            if (challenge == null || challenge.ExpiresAt <= DateTime.UtcNow)
                return BadRequest(new { error = "Invalid or expired enrollment challenge" });

            if (!_mfaService.VerifyCode(challenge.Secret, request.Code))
                return BadRequest(new { error = "Invalid MFA code" });

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null)
                return Unauthorized(new { error = "Invalid authenticated context" });

            user.MfaEnabled = true;
            user.MfaSecret = challenge.Secret;
            user.MfaEnabledAt = DateTime.UtcNow;
            challenge.ConsumedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync("USER_MFA_ENROLLED", nameof(User), userId.ToString(), new { tenantId });
            return Ok(new { message = "MFA enrollment complete." });
        }

        [HttpPost("mfa/step-up")]
        [Authorize]
        public async Task<IActionResult> StepUpAuthentication([FromBody] StepUpAuthenticationRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
                return BadRequest(new { error = "Code is required" });

            var tenantId = ResolveCurrentTenantId();
            var userId = ResolveCurrentUserId();
            if (tenantId == Guid.Empty || userId == Guid.Empty)
                return Unauthorized(new { error = "Invalid authenticated context" });

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null)
                return Unauthorized(new { error = "Invalid authenticated context" });
            if (!user.MfaEnabled || string.IsNullOrWhiteSpace(user.MfaSecret))
                return BadRequest(new { error = "MFA is not enrolled for this account" });

            if (!_mfaService.VerifyCode(user.MfaSecret, request.Code))
                return Unauthorized(new { error = "Invalid MFA code" });

            var purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "admin_sensitive" : request.Purpose.Trim().ToLowerInvariant();
            var stepUpToken = _mfaService.GenerateOpaqueToken();
            _dbContext.UserStepUpSessions.Add(new UserStepUpSession
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Purpose = purpose,
                SessionTokenHash = _mfaService.HashToken(stepUpToken),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync("USER_MFA_STEP_UP_VERIFIED", nameof(User), userId.ToString(), new { tenantId, purpose });
            return Ok(new { stepUpToken, purpose, expiresAt = DateTime.UtcNow.AddMinutes(10) });
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

        private Guid ResolveCurrentTenantId()
        {
            var claim = User.FindFirst("tenant_id")?.Value;
            return Guid.TryParse(claim, out var tenantId) ? tenantId : Guid.Empty;
        }

        private Guid ResolveCurrentUserId()
        {
            var claimTypesToCheck = new[]
            {
                "sub",
                JwtRegisteredClaimNames.Sub,
                ClaimTypes.NameIdentifier
            };

            foreach (var claimType in claimTypesToCheck)
            {
                var claimValue = User.FindFirst(claimType)?.Value;
                if (Guid.TryParse(claimValue, out var userId))
                    return userId;
            }

            return Guid.Empty;
        }

        private async Task<IActionResult?> ValidateStepUpOrForbidAsync(string purpose, CancellationToken cancellationToken)
        {
            var tenantId = ResolveCurrentTenantId();
            var userId = ResolveCurrentUserId();
            if (tenantId == Guid.Empty || userId == Guid.Empty)
                return null;

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null)
                return null;
            if (!user.MfaEnabled)
                return null;

            var stepUpToken = Request.Headers["X-Step-Up-Token"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(stepUpToken))
                return StatusCode(403, new { error = "Step-up authentication required" });

            var tokenHash = _mfaService.HashToken(stepUpToken.Trim());
            var session = await _dbContext.UserStepUpSessions.FirstOrDefaultAsync(s =>
                s.TenantId == tenantId &&
                s.UserId == userId &&
                s.Purpose == purpose &&
                s.SessionTokenHash == tokenHash, cancellationToken);

            if (session == null || session.ExpiresAt <= DateTime.UtcNow)
                return StatusCode(403, new { error = "Invalid or expired step-up token" });

            return null;
        }
    }
}
