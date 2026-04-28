using Domain.Authorization;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Outputs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Presentation.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Presentation.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthOrchestrationService _authOrchestrationService;
        private readonly IAuditService _auditService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ITenantContext _tenantContext;
        private readonly IIdentityLifecycleService _identityLifecycleService;

        public AuthController(
            IAuthOrchestrationService authOrchestrationService,
            IAuditService auditService,
            IRefreshTokenService refreshTokenService,
            ITenantContext tenantContext,
            IIdentityLifecycleService identityLifecycleService)
        {
            _authOrchestrationService = authOrchestrationService;
            _auditService = auditService;
            _refreshTokenService = refreshTokenService;
            _tenantContext = tenantContext;
            _identityLifecycleService = identityLifecycleService;
        }

        [HttpPost("register")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.UnauthenticatedAuthEndpoints)]
        public async Task<IActionResult> Register(RegisterTenantRequest request, CancellationToken cancellationToken)
        {
            var result = await _authOrchestrationService.RegisterAsync(
                request,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            return result.Error switch
            {
                AuthFlowError.SubdomainAlreadyTaken => BadRequest(new { error = "Subdomain already taken" }),
                AuthFlowError.EmailAlreadyRegistered => BadRequest(new { error = "Email already registered" }),
                AuthFlowError.None when result.Response is not null => Ok(result.Response),
                _ => StatusCode(500, new { error = "Registration failed" })
            };
        }

        [HttpPost("login")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.UnauthenticatedAuthEndpoints)]
        public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await _authOrchestrationService.LoginAsync(
                request,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            return result.Error switch
            {
                AuthFlowError.InvalidCredentials => Unauthorized(new { error = "Invalid credentials" }),
                AuthFlowError.EmailNotVerified => Unauthorized(new { error = "Email is not verified" }),
                AuthFlowError.TenantSuspended => StatusCode(403, new { error = "Tenant account is suspended" }),
                AuthFlowError.MfaChallengeRequired => Unauthorized(new { error = "MFA challenge required", requiresMfa = true }),
                AuthFlowError.None when result.Response is not null => Ok(result.Response),
                _ => Unauthorized(new { error = "Invalid credentials" })
            };
        }

        [HttpPost("refresh")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.UnauthenticatedAuthEndpoints)]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            if (request.TenantId == Guid.Empty)
                return BadRequest(new { error = "TenantId is required" });

            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return BadRequest(new { error = "RefreshToken is required" });

            var result = await _authOrchestrationService.RefreshAsync(
                request.TenantId,
                request.RefreshToken,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            return result.Error switch
            {
                AuthFlowError.InvalidRefreshTokenContext => Unauthorized(new { error = "Invalid refresh token context" }),
                AuthFlowError.InvalidOrExpiredRefreshToken => Unauthorized(new { error = "Invalid or expired refresh token" }),
                AuthFlowError.None when result.Response is not null => Ok(result.Response),
                _ => Unauthorized(new { error = "Invalid or expired refresh token" })
            };
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

            await _auditService.LogAsync("USER_SESSIONS_REVOKED_ALL", "User", effectiveUserId.ToString(), new
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

                await _auditService.LogAsync("USER_INVITE_CREATED", "User", actorUserId.ToString(), new
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

            await _auditService.LogAsync("USER_INVITE_ACCEPTED", "User", userId.Value.ToString(), new
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

            var result = await _authOrchestrationService.InitiateMfaEnrollmentAsync(tenantId, userId, cancellationToken);
            return result.Error switch
            {
                AuthFlowError.InvalidAuthenticatedContext => Unauthorized(new { error = "Invalid authenticated context" }),
                AuthFlowError.MfaAlreadyEnabled => BadRequest(new { error = "MFA is already enabled" }),
                AuthFlowError.None => Ok(new
                {
                    enrollmentToken = result.EnrollmentToken,
                    secret = result.Secret,
                    provisioningUri = result.ProvisioningUri,
                    expiresAt = result.ExpiresAt
                }),
                _ => StatusCode(500, new { error = "MFA enrollment initiation failed" })
            };
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

            var result = await _authOrchestrationService.CompleteMfaEnrollmentAsync(
                tenantId,
                userId,
                request.EnrollmentToken,
                request.Code,
                cancellationToken);

            return result.Error switch
            {
                AuthFlowError.InvalidAuthenticatedContext => Unauthorized(new { error = "Invalid authenticated context" }),
                AuthFlowError.InvalidOrExpiredEnrollmentChallenge => BadRequest(new { error = "Invalid or expired enrollment challenge" }),
                AuthFlowError.InvalidMfaCode => BadRequest(new { error = "Invalid MFA code" }),
                AuthFlowError.None => Ok(new { message = "MFA enrollment complete." }),
                _ => StatusCode(500, new { error = "MFA enrollment failed" })
            };
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

            var result = await _authOrchestrationService.StepUpAuthenticationAsync(
                tenantId,
                userId,
                request.Code,
                request.Purpose,
                cancellationToken);

            return result.Error switch
            {
                AuthFlowError.InvalidAuthenticatedContext => Unauthorized(new { error = "Invalid authenticated context" }),
                AuthFlowError.MfaNotEnrolled => BadRequest(new { error = "MFA is not enrolled for this account" }),
                AuthFlowError.InvalidMfaCode => Unauthorized(new { error = "Invalid MFA code" }),
                AuthFlowError.None => Ok(new { stepUpToken = result.StepUpToken, purpose = result.Purpose, expiresAt = result.ExpiresAt }),
                _ => StatusCode(500, new { error = "Step-up authentication failed" })
            };
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

            await _auditService.LogAsync(auditAction, "User", activeToken.UserId.ToString(), new
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

            var stepUpToken = Request.Headers["X-Step-Up-Token"].FirstOrDefault();
            var validation = await _authOrchestrationService.ValidateStepUpAsync(tenantId, userId, purpose, stepUpToken, cancellationToken);
            if (validation.IsValid)
                return null;

            return validation.Error switch
            {
                AuthFlowError.StepUpRequired => StatusCode(403, new { error = "Step-up authentication required" }),
                AuthFlowError.InvalidOrExpiredStepUpToken => StatusCode(403, new { error = "Invalid or expired step-up token" }),
                _ => null
            };
        }
    }
}
