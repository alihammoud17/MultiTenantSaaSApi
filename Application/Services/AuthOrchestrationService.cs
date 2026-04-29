using Domain.DTOs;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Outputs;
using Domain.Responses;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class AuthOrchestrationService : IAuthOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IJwtService _jwtService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IAuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly IMfaService _mfaService;
        private readonly ILogger<AuthOrchestrationService> _logger;

        public AuthOrchestrationService(
            ApplicationDbContext dbContext,
            IJwtService jwtService,
            IRefreshTokenService refreshTokenService,
            IAuditService auditService,
            ITenantContext tenantContext,
            IMfaService mfaService,
            ILogger<AuthOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _jwtService = jwtService;
            _refreshTokenService = refreshTokenService;
            _auditService = auditService;
            _tenantContext = tenantContext;
            _mfaService = mfaService;
            _logger = logger;
        }

        public async Task<RegisterAuthResult> RegisterAsync(RegisterTenantRequest request, string? requestIp, CancellationToken cancellationToken = default)
        {
            if (await _dbContext.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain, cancellationToken))
                return new RegisterAuthResult(false, AuthFlowError.SubdomainAlreadyTaken, null);

            if (await _dbContext.Users.AnyAsync(u => u.Email == request.AdminEmail, cancellationToken))
                return new RegisterAuthResult(false, AuthFlowError.EmailAlreadyRegistered, null);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var tenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = request.CompanyName,
                    Subdomain = request.Subdomain,
                    Status = TenantStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _dbContext.Tenants.AddAsync(tenant, cancellationToken);

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    Email = request.AdminEmail,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
                    EmailVerifiedAt = now,
                    Role = "ADMIN",
                    CreatedAt = now
                };
                await _dbContext.Users.AddAsync(user, cancellationToken);

                await _dbContext.Subscriptions.AddAsync(new Subscription
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    PlanId = "plan-free",
                    Status = SubscriptionStatus.Active,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = now.AddMonths(1),
                    CreatedAt = now
                }, cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var accessToken = _jwtService.GenerateToken(user, tenant);
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
                    now.AddDays(7),
                    requestIp);

                _logger.LogInformation("New tenant registered: {TenantId}", tenant.Id);

                return new RegisterAuthResult(
                    true,
                    AuthFlowError.None,
                    new AuthResponse(
                        accessToken,
                        refreshTokenResult.Token,
                        tenant.Id,
                        user.Id,
                        user.Email,
                        now.AddMinutes(60),
                        refreshTokenResult.ExpiresAt));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to register tenant");
                return new RegisterAuthResult(false, AuthFlowError.RegistrationFailed, null);
            }
        }

        public async Task<LoginAuthResult> LoginAsync(LoginRequest request, string? requestIp, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return new LoginAuthResult(false, AuthFlowError.InvalidCredentials, null);

            if (!user.EmailVerifiedAt.HasValue)
                return new LoginAuthResult(false, AuthFlowError.EmailNotVerified, null);

            if (user.Tenant.Status != TenantStatus.Active)
                return new LoginAuthResult(false, AuthFlowError.TenantSuspended, null);

            if (user.MfaEnabled)
                return new LoginAuthResult(false, AuthFlowError.MfaChallengeRequired, null, true);

            var now = DateTime.UtcNow;
            user.LastLoginAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);

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
                now.AddDays(7),
                requestIp);

            return new LoginAuthResult(
                true,
                AuthFlowError.None,
                new AuthResponse(
                    token,
                    refreshTokenResult.Token,
                    user.TenantId,
                    user.Id,
                    user.Email,
                    now.AddMinutes(60),
                    refreshTokenResult.ExpiresAt));
        }

        public async Task<RefreshAuthResult> RefreshAsync(Guid tenantId, string refreshToken, string? requestIp, CancellationToken cancellationToken = default)
        {
            _tenantContext.SetTenantId(tenantId);
            var activeToken = await _refreshTokenService.GetActiveTokenAsync(tenantId, refreshToken, cancellationToken);
            if (activeToken == null)
                return new RefreshAuthResult(false, AuthFlowError.InvalidOrExpiredRefreshToken, null);

            var user = await _dbContext.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == activeToken.UserId && u.TenantId == tenantId, cancellationToken);

            if (user == null || user.Tenant.Status != TenantStatus.Active)
                return new RefreshAuthResult(false, AuthFlowError.InvalidRefreshTokenContext, null);

            var rotated = await _refreshTokenService.RotateTokenAsync(
                tenantId,
                refreshToken,
                DateTime.UtcNow.AddDays(7),
                requestIp,
                cancellationToken);

            if (rotated == null)
                return new RefreshAuthResult(false, AuthFlowError.InvalidOrExpiredRefreshToken, null);

            var accessToken = _jwtService.GenerateToken(user, user.Tenant);
            await _auditService.LogAsync("USER_TOKEN_REFRESHED", nameof(User), user.Id.ToString(), new
            {
                user.Email
            });

            return new RefreshAuthResult(
                true,
                AuthFlowError.None,
                new AuthResponse(
                    accessToken,
                    rotated.Token,
                    user.TenantId,
                    user.Id,
                    user.Email,
                    DateTime.UtcNow.AddMinutes(60),
                    rotated.ExpiresAt));
        }

        public async Task<InitiateMfaEnrollmentResult> InitiateMfaEnrollmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null)
                return new InitiateMfaEnrollmentResult(false, AuthFlowError.InvalidAuthenticatedContext, null, null, null, null);
            if (user.MfaEnabled)
                return new InitiateMfaEnrollmentResult(false, AuthFlowError.MfaAlreadyEnabled, null, null, null, null);

            var (secret, provisioningUri) = _mfaService.GenerateEnrollmentSecret("MultiTenantSaaSApi", user.Email);
            var enrollmentToken = _mfaService.GenerateOpaqueToken();
            var expiresAt = DateTime.UtcNow.AddMinutes(10);

            _dbContext.UserMfaEnrollmentChallenges.Add(new UserMfaEnrollmentChallenge
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                EnrollmentTokenHash = _mfaService.HashToken(enrollmentToken),
                Secret = secret,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new InitiateMfaEnrollmentResult(true, AuthFlowError.None, enrollmentToken, secret, provisioningUri, expiresAt);
        }

        public async Task<CompleteMfaEnrollmentResult> CompleteMfaEnrollmentAsync(Guid tenantId, Guid userId, string enrollmentToken, string code, CancellationToken cancellationToken = default)
        {
            var tokenHash = _mfaService.HashToken(enrollmentToken.Trim());
            var challenge = await _dbContext.UserMfaEnrollmentChallenges
                .FirstOrDefaultAsync(c =>
                    c.TenantId == tenantId &&
                    c.UserId == userId &&
                    c.EnrollmentTokenHash == tokenHash &&
                    c.ConsumedAt == null, cancellationToken);

            if (challenge == null || challenge.ExpiresAt <= DateTime.UtcNow)
                return new CompleteMfaEnrollmentResult(false, AuthFlowError.InvalidOrExpiredEnrollmentChallenge);

            if (!_mfaService.VerifyCode(challenge.Secret, code))
                return new CompleteMfaEnrollmentResult(false, AuthFlowError.InvalidMfaCode);

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null)
                return new CompleteMfaEnrollmentResult(false, AuthFlowError.InvalidAuthenticatedContext);

            user.MfaEnabled = true;
            user.MfaSecret = challenge.Secret;
            user.MfaEnabledAt = DateTime.UtcNow;
            challenge.ConsumedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync("USER_MFA_ENROLLED", nameof(User), userId.ToString(), new { tenantId });
            return new CompleteMfaEnrollmentResult(true, AuthFlowError.None);
        }

        public async Task<StepUpAuthenticationResult> StepUpAuthenticationAsync(Guid tenantId, Guid userId, string code, string? purpose, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null)
                return new StepUpAuthenticationResult(false, AuthFlowError.InvalidAuthenticatedContext, null, null, null);
            if (!user.MfaEnabled || string.IsNullOrWhiteSpace(user.MfaSecret))
                return new StepUpAuthenticationResult(false, AuthFlowError.MfaNotEnrolled, null, null, null);
            if (!_mfaService.VerifyCode(user.MfaSecret, code))
                return new StepUpAuthenticationResult(false, AuthFlowError.InvalidMfaCode, null, null, null);

            var normalizedPurpose = string.IsNullOrWhiteSpace(purpose) ? "admin_sensitive" : purpose.Trim().ToLowerInvariant();
            var stepUpToken = _mfaService.GenerateOpaqueToken();
            var expiresAt = DateTime.UtcNow.AddMinutes(10);
            _dbContext.UserStepUpSessions.Add(new UserStepUpSession
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Purpose = normalizedPurpose,
                SessionTokenHash = _mfaService.HashToken(stepUpToken),
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync("USER_MFA_STEP_UP_VERIFIED", nameof(User), userId.ToString(), new { tenantId, purpose = normalizedPurpose });
            return new StepUpAuthenticationResult(true, AuthFlowError.None, stepUpToken, normalizedPurpose, expiresAt);
        }

        public async Task<StepUpValidationResult> ValidateStepUpAsync(Guid tenantId, Guid userId, string purpose, string? stepUpToken, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);
            if (user == null || !user.MfaEnabled)
                return new StepUpValidationResult(true, false, AuthFlowError.None);

            if (string.IsNullOrWhiteSpace(stepUpToken))
                return new StepUpValidationResult(false, true, AuthFlowError.StepUpRequired);

            var tokenHash = _mfaService.HashToken(stepUpToken.Trim());
            var session = await _dbContext.UserStepUpSessions.FirstOrDefaultAsync(s =>
                s.TenantId == tenantId &&
                s.UserId == userId &&
                s.Purpose == purpose &&
                s.SessionTokenHash == tokenHash, cancellationToken);

            if (session == null || session.ExpiresAt <= DateTime.UtcNow)
                return new StepUpValidationResult(false, true, AuthFlowError.InvalidOrExpiredStepUpToken);

            return new StepUpValidationResult(true, true, AuthFlowError.None);
        }
    }
}
