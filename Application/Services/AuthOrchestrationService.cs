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
        // How long a self-registered tenant waits for its admin to verify their email. After this, its subdomain
        // and email can be reclaimed by a new registration, so an unverified sign-up cannot hold them for good.
        public static readonly TimeSpan PendingRegistrationLifetime = TimeSpan.FromHours(48);

        // Verified against when a login email is unknown, so that path costs the same as a wrong password.
        // HashPassword uses the same default work factor as real user hashes.
        private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

        private readonly ApplicationDbContext _dbContext;
        private readonly IJwtService _jwtService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IAuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly IMfaService _mfaService;
        private readonly IIdentityNotificationService _notificationService;
        private readonly ILogger<AuthOrchestrationService> _logger;

        public AuthOrchestrationService(
            ApplicationDbContext dbContext,
            IJwtService jwtService,
            IRefreshTokenService refreshTokenService,
            IAuditService auditService,
            ITenantContext tenantContext,
            IMfaService mfaService,
            IIdentityNotificationService notificationService,
            ILogger<AuthOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _jwtService = jwtService;
            _refreshTokenService = refreshTokenService;
            _auditService = auditService;
            _tenantContext = tenantContext;
            _mfaService = mfaService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<RegisterAuthResult> RegisterAsync(RegisterTenantRequest request, string? requestIp, CancellationToken cancellationToken = default)
        {
            var validation = TenantRegistrationValidator.Validate(request);
            if (!validation.IsValid)
                return new RegisterAuthResult(false, AuthFlowError.InvalidRegistrationInput, null, validation.Error);

            request = validation.NormalizedRequest!;

            // Compare case-insensitively so rows stored before normalization still count as duplicates.
            var subdomainLookup = request.Subdomain.ToLowerInvariant();
            var emailLookup = request.AdminEmail.ToLowerInvariant();

            await ReclaimExpiredPendingRegistrationsAsync(subdomainLookup, emailLookup, cancellationToken);

            if (await _dbContext.Tenants.AnyAsync(t => t.Subdomain.ToLower() == subdomainLookup, cancellationToken))
                return new RegisterAuthResult(false, AuthFlowError.SubdomainAlreadyTaken, null);

            // Checked after the subdomain so the subdomain answer never depends on the email.
            if (await _dbContext.Users.AnyAsync(u => u.Email.ToLower() == emailLookup, cancellationToken))
                return await AnswerExistingAccountRegistrationAsync(request, cancellationToken);

            var now = DateTime.UtcNow;
            var verificationToken = IdentityTokenGenerator.GenerateToken();
            var verificationExpiresAt = now.Add(PendingRegistrationLifetime);
            Tenant tenant;
            User user;

            await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    // The tenant stays inert (no login, refresh or tenant-scoped requests) until the admin verifies.
                    tenant = new Tenant
                    {
                        Id = Guid.NewGuid(),
                        Name = request.CompanyName,
                        Subdomain = request.Subdomain,
                        Status = TenantStatus.PendingVerification,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    await _dbContext.Tenants.AddAsync(tenant, cancellationToken);

                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        Email = request.AdminEmail,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
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

                    await _dbContext.UserVerificationTokens.AddAsync(new UserVerificationToken
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        UserId = user.Id,
                        TokenHash = IdentityTokenGenerator.ComputeHash(verificationToken),
                        CreatedAt = now,
                        ExpiresAt = verificationExpiresAt
                    }, cancellationToken);

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    // Written inside the transaction so a registration is never committed without its audit entry.
                    _tenantContext.SetTenantId(tenant.Id);
                    await _auditService.LogAsync("TENANT_REGISTERED", nameof(Tenant), tenant.Id.ToString(), new
                    {
                        tenant.Name,
                        tenant.Subdomain,
                        AdminUserId = user.Id,
                        Status = tenant.Status.ToString()
                    }, actorUserId: user.Id);

                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Failed to register tenant");
                    return new RegisterAuthResult(false, AuthFlowError.RegistrationFailed, null);
                }
            }

            _logger.LogInformation("New tenant registered pending email verification: {TenantId}", tenant.Id);

            try
            {
                await _notificationService.SendVerificationAsync(tenant.Id, user.Email, verificationToken, verificationExpiresAt, cancellationToken);
            }
            catch (Exception ex)
            {
                // The registration is committed; the admin can ask for a new token via /auth/verification/request.
                _logger.LogError(ex, "Failed to send registration verification for tenant {TenantId}", tenant.Id);
            }

            return new RegisterAuthResult(
                true,
                AuthFlowError.None,
                new RegistrationPendingResponse(
                    tenant.Id,
                    RegistrationPendingResponse.PendingVerificationStatus,
                    verificationExpiresAt));
        }

        // Anonymous callers must not learn whether an email has an account, so a known email gets the same 202 shape
        // as a new registration (with a tenant id that does not exist) and roughly the same cost, but nothing is
        // written. The real owner is told about the attempt instead.
        private async Task<RegisterAuthResult> AnswerExistingAccountRegistrationAsync(RegisterTenantRequest request, CancellationToken cancellationToken)
        {
            _ = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword);

            try
            {
                await _notificationService.SendExistingAccountRegistrationNoticeAsync(request.AdminEmail, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send existing-account registration notice");
            }

            return new RegisterAuthResult(
                true,
                AuthFlowError.None,
                new RegistrationPendingResponse(
                    Guid.NewGuid(),
                    RegistrationPendingResponse.PendingVerificationStatus,
                    DateTime.UtcNow.Add(PendingRegistrationLifetime)));
        }

        // Deletes unverified registrations past their pending window that hold the requested subdomain or email.
        // Tenant foreign keys cascade, so the tenant's user, tokens and subscription go with it.
        private async Task ReclaimExpiredPendingRegistrationsAsync(string subdomainLookup, string emailLookup, CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow - PendingRegistrationLifetime;
            var reclaimed = await _dbContext.Tenants
                .Where(t => t.Status == TenantStatus.PendingVerification
                    && t.CreatedAt < cutoff
                    && (t.Subdomain.ToLower() == subdomainLookup || t.Users.Any(u => u.Email.ToLower() == emailLookup)))
                .ExecuteDeleteAsync(cancellationToken);

            if (reclaimed > 0)
                _logger.LogInformation("Reclaimed {Count} expired pending tenant registration(s)", reclaimed);
        }

        public async Task<LoginAuthResult> LoginAsync(LoginRequest request, string? requestIp, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

            if (user == null)
            {
                // Same cost as a wrong password, so response time does not reveal whether the email exists.
                BCrypt.Net.BCrypt.Verify(request.Password, DummyPasswordHash);
                return new LoginAuthResult(false, AuthFlowError.InvalidCredentials, null);
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
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
            }, actorUserId: user.Id);

            var accessToken = _jwtService.GenerateToken(user, user.Tenant);
            var refreshTokenResult = await _refreshTokenService.IssueTokenAsync(
                user.TenantId,
                user.Id,
                now.AddDays(7),
                requestIp);

            return new LoginAuthResult(
                true,
                AuthFlowError.None,
                new AuthResponse(
                    accessToken.Token,
                    refreshTokenResult.Token,
                    user.TenantId,
                    user.Id,
                    user.Email,
                    accessToken.ExpiresAt,
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
            }, actorUserId: user.Id);

            return new RefreshAuthResult(
                true,
                AuthFlowError.None,
                new AuthResponse(
                    accessToken.Token,
                    rotated.Token,
                    user.TenantId,
                    user.Id,
                    user.Email,
                    accessToken.ExpiresAt,
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
