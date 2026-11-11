using Application.Services;
using Domain.DTOs;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Outputs;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.UnitTests;

/// <summary>
/// Failure and edge branches of <see cref="AuthOrchestrationService"/> that the HTTP suites do not reach:
/// notification failures, refresh-rotation races, and every rejection path of MFA enrollment and step-up.
/// </summary>
public sealed class AuthOrchestrationServiceTests : IAsyncLifetime
{
    private const string Password = "Passw0rd!";

    private readonly FakeMfaService _mfa = new();
    private readonly FakeRefreshTokenService _refreshTokens = new();
    private readonly ThrowingNotificationService _notifications = new();
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Register_ShouldSucceed_WhenTheVerificationEmailCannotBeSent()
    {
        _notifications.Fail = true;

        var result = await CreateService().RegisterAsync(NewRegistration("notify-fails"), "198.51.100.1");

        result.Succeeded.Should().BeTrue();
        (await _db.Tenants.SingleAsync(t => t.Id == result.Response!.TenantId)).Status.Should().Be(TenantStatus.PendingVerification);
    }

    [Fact]
    public async Task Register_ForKnownEmail_ShouldAnswerLikeANewRegistration_EvenWhenTheNoticeFails()
    {
        var existing = await SeedUserAsync();
        _notifications.Fail = true;
        var tenantCount = await _db.Tenants.CountAsync();

        var result = await CreateService().RegisterAsync(NewRegistration("known-email", existing.Email), null);

        result.Succeeded.Should().BeTrue();
        result.Response!.TenantId.Should().NotBe(existing.TenantId);
        (await _db.Tenants.CountAsync()).Should().Be(tenantCount, "nothing is written for a known email");
    }

    [Fact]
    public async Task Register_ShouldReportFailure_AndWriteNothing_WhenTheAuditWriteFails()
    {
        var service = CreateService(new ThrowingAuditService());

        var result = await service.RegisterAsync(NewRegistration("audit-fails"), null);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(AuthFlowError.RegistrationFailed);
        _db.ChangeTracker.Clear();
        (await _db.Tenants.AnyAsync(t => t.Subdomain == "audit-fails")).Should().BeFalse("the registration transaction is rolled back");
    }

    [Fact]
    public async Task Login_ForMfaEnabledUser_ShouldRequireTheMfaChallenge_WithoutIssuingTokens()
    {
        var user = await SeedUserAsync(mfaSecret: "SECRET");

        var result = await CreateService().LoginAsync(new LoginRequest(user.Email, Password), null);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(AuthFlowError.MfaChallengeRequired);
        result.RequiresMfa.Should().BeTrue();
        _refreshTokens.Issued.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_ShouldFail_WhenTheTokenIsRotatedConcurrently()
    {
        var user = await SeedUserAsync();
        _refreshTokens.Active = new RefreshToken { Id = Guid.NewGuid(), TenantId = user.TenantId, UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) };
        _refreshTokens.RotateResult = null;

        var result = await CreateService().RefreshAsync(user.TenantId, "refresh-token", null);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(AuthFlowError.InvalidOrExpiredRefreshToken);
    }

    [Fact]
    public async Task Refresh_ShouldRejectATokenOwnedByAUserOfAnotherTenant()
    {
        var owner = await SeedUserAsync();
        var otherTenant = await SeedUserAsync();
        _refreshTokens.Active = new RefreshToken { Id = Guid.NewGuid(), TenantId = otherTenant.TenantId, UserId = owner.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) };

        var result = await CreateService().RefreshAsync(otherTenant.TenantId, "refresh-token", null);

        result.Error.Should().Be(AuthFlowError.InvalidRefreshTokenContext);
    }

    [Fact]
    public async Task InitiateMfaEnrollment_ShouldRejectUnknownUsers_AndUsersAlreadyEnrolled()
    {
        var enrolled = await SeedUserAsync(mfaSecret: "SECRET");
        var service = CreateService();

        (await service.InitiateMfaEnrollmentAsync(enrolled.TenantId, Guid.NewGuid())).Error.Should().Be(AuthFlowError.InvalidAuthenticatedContext);
        (await service.InitiateMfaEnrollmentAsync(Guid.NewGuid(), enrolled.Id)).Error.Should().Be(AuthFlowError.InvalidAuthenticatedContext, "the user id is scoped to its tenant");
        (await service.InitiateMfaEnrollmentAsync(enrolled.TenantId, enrolled.Id)).Error.Should().Be(AuthFlowError.MfaAlreadyEnabled);
    }

    [Fact]
    public async Task CompleteMfaEnrollment_ShouldRejectUnknownExpiredConsumedOrForeignChallenges()
    {
        var user = await SeedUserAsync();
        var other = await SeedUserAsync();
        var service = CreateService();
        var initiated = await service.InitiateMfaEnrollmentAsync(user.TenantId, user.Id);

        (await service.CompleteMfaEnrollmentAsync(user.TenantId, user.Id, "unknown-token", "123456")).Error
            .Should().Be(AuthFlowError.InvalidOrExpiredEnrollmentChallenge);
        (await service.CompleteMfaEnrollmentAsync(other.TenantId, other.Id, initiated.EnrollmentToken!, "123456")).Error
            .Should().Be(AuthFlowError.InvalidOrExpiredEnrollmentChallenge, "a challenge belongs to the user who started it");

        _mfa.AcceptCodes = false;
        (await service.CompleteMfaEnrollmentAsync(user.TenantId, user.Id, initiated.EnrollmentToken!, "000000")).Error
            .Should().Be(AuthFlowError.InvalidMfaCode);

        _mfa.AcceptCodes = true;
        (await service.CompleteMfaEnrollmentAsync(user.TenantId, user.Id, $"  {initiated.EnrollmentToken}  ", "123456")).Succeeded
            .Should().BeTrue("the token is trimmed");
        (await service.CompleteMfaEnrollmentAsync(user.TenantId, user.Id, initiated.EnrollmentToken!, "123456")).Error
            .Should().Be(AuthFlowError.InvalidOrExpiredEnrollmentChallenge, "a challenge is single-use");

        var stored = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        stored.MfaEnabled.Should().BeTrue();
        stored.MfaSecret.Should().Be(initiated.Secret);
    }

    [Fact]
    public async Task CompleteMfaEnrollment_ShouldRejectAnExpiredChallenge()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var initiated = await service.InitiateMfaEnrollmentAsync(user.TenantId, user.Id);
        await _db.UserMfaEnrollmentChallenges.ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        _db.ChangeTracker.Clear();

        var result = await service.CompleteMfaEnrollmentAsync(user.TenantId, user.Id, initiated.EnrollmentToken!, "123456");

        result.Error.Should().Be(AuthFlowError.InvalidOrExpiredEnrollmentChallenge);
    }

    [Fact]
    public async Task CompleteMfaEnrollment_ShouldRejectAChallengeWhoseUserWasRemoved()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var initiated = await service.InitiateMfaEnrollmentAsync(user.TenantId, user.Id);
        _db.ChangeTracker.Clear();
        await _db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await _db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();

        var result = await service.CompleteMfaEnrollmentAsync(user.TenantId, user.Id, initiated.EnrollmentToken!, "123456");

        result.Error.Should().Be(AuthFlowError.InvalidAuthenticatedContext);
    }

    [Fact]
    public async Task StepUp_ShouldRejectUnknownUsers_UnenrolledUsers_AndWrongCodes()
    {
        var unenrolled = await SeedUserAsync();
        var enrolled = await SeedUserAsync(mfaSecret: "SECRET");
        var service = CreateService();

        (await service.StepUpAuthenticationAsync(enrolled.TenantId, Guid.NewGuid(), "123456", null)).Error.Should().Be(AuthFlowError.InvalidAuthenticatedContext);
        (await service.StepUpAuthenticationAsync(unenrolled.TenantId, unenrolled.Id, "123456", null)).Error.Should().Be(AuthFlowError.MfaNotEnrolled);

        _mfa.AcceptCodes = false;
        (await service.StepUpAuthenticationAsync(enrolled.TenantId, enrolled.Id, "000000", null)).Error.Should().Be(AuthFlowError.InvalidMfaCode);
        (await _db.UserStepUpSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task StepUp_ShouldIssueAPurposeBoundSession_ThatValidatesOnlyForThatUserAndPurpose()
    {
        var user = await SeedUserAsync(mfaSecret: "SECRET");
        var other = await SeedUserAsync(mfaSecret: "SECRET");
        var service = CreateService();

        var defaultPurpose = await service.StepUpAuthenticationAsync(user.TenantId, user.Id, "123456", "  ");
        var stepUp = await service.StepUpAuthenticationAsync(user.TenantId, user.Id, "123456", "  Billing_Manage ");

        defaultPurpose.Purpose.Should().Be("admin_sensitive");
        stepUp.Purpose.Should().Be("billing_manage");

        (await service.ValidateStepUpAsync(user.TenantId, user.Id, "billing_manage", stepUp.StepUpToken)).IsValid.Should().BeTrue();
        (await service.ValidateStepUpAsync(user.TenantId, user.Id, "billing_manage", $" {stepUp.StepUpToken} ")).IsValid.Should().BeTrue();
        (await service.ValidateStepUpAsync(user.TenantId, user.Id, "admin_sensitive", stepUp.StepUpToken)).Error
            .Should().Be(AuthFlowError.InvalidOrExpiredStepUpToken, "the session is bound to its purpose");
        (await service.ValidateStepUpAsync(other.TenantId, other.Id, "billing_manage", stepUp.StepUpToken)).Error
            .Should().Be(AuthFlowError.InvalidOrExpiredStepUpToken, "the session is bound to its user and tenant");
    }

    [Fact]
    public async Task ValidateStepUp_ShouldRequireATokenOnlyForMfaUsers_AndRejectExpiredSessions()
    {
        var unenrolled = await SeedUserAsync();
        var enrolled = await SeedUserAsync(mfaSecret: "SECRET");
        var service = CreateService();

        var notRequired = await service.ValidateStepUpAsync(unenrolled.TenantId, unenrolled.Id, "admin_sensitive", null);
        notRequired.IsValid.Should().BeTrue();
        notRequired.IsRequired.Should().BeFalse();

        var missing = await service.ValidateStepUpAsync(enrolled.TenantId, enrolled.Id, "admin_sensitive", " ");
        missing.IsValid.Should().BeFalse();
        missing.IsRequired.Should().BeTrue();
        missing.Error.Should().Be(AuthFlowError.StepUpRequired);

        var stepUp = await service.StepUpAuthenticationAsync(enrolled.TenantId, enrolled.Id, "123456", null);
        await _db.UserStepUpSessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        _db.ChangeTracker.Clear();

        (await service.ValidateStepUpAsync(enrolled.TenantId, enrolled.Id, "admin_sensitive", stepUp.StepUpToken)).Error
            .Should().Be(AuthFlowError.InvalidOrExpiredStepUpToken);
    }

    private AuthOrchestrationService CreateService(IAuditService? auditService = null)
    {
        var tenantContext = new TenantContext();
        return new AuthOrchestrationService(
            _db,
            new FakeJwtService(),
            _refreshTokens,
            auditService ?? new AuditService(_db, tenantContext, new HttpContextAccessor()),
            tenantContext,
            _mfa,
            _notifications,
            NullLogger<AuthOrchestrationService>.Instance);
    }

    private static RegisterTenantRequest NewRegistration(string subdomain, string? email = null) =>
        new($"Company {subdomain}", subdomain, email ?? $"{subdomain}-{Guid.NewGuid():N}@example.com", Password);

    private async Task<User> SeedUserAsync(string? mfaSecret = null)
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Auth Tenant", Subdomain = $"auth-{Guid.NewGuid():N}", Status = TenantStatus.Active, CreatedAt = now, UpdatedAt = now };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4),
            EmailVerifiedAt = now,
            Role = "ADMIN",
            CreatedAt = now,
            MfaEnabled = mfaSecret is not null,
            MfaSecret = mfaSecret
        };
        _db.Tenants.Add(tenant);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return user;
    }

    private sealed class FakeMfaService : IMfaService
    {
        private readonly MfaService _real = new();

        public bool AcceptCodes { get; set; } = true;

        public (string Secret, string ProvisioningUri) GenerateEnrollmentSecret(string issuer, string accountName) => _real.GenerateEnrollmentSecret(issuer, accountName);
        public bool VerifyCode(string secret, string code) => AcceptCodes;
        public string GenerateOpaqueToken() => _real.GenerateOpaqueToken();
        public string HashToken(string token) => _real.HashToken(token);
    }

    private sealed class FakeJwtService : IJwtService
    {
        public AccessToken GenerateToken(User user, Tenant tenant) => new($"jwt-{user.Id}", DateTime.UtcNow.AddHours(1));
    }

    private sealed class FakeRefreshTokenService : IRefreshTokenService
    {
        public int Issued { get; private set; }
        public RefreshToken? Active { get; set; }
        public RefreshTokenIssueResult? RotateResult { get; set; } = new("rotated", Guid.NewGuid(), DateTime.UtcNow.AddDays(7));

        public Task<RefreshTokenIssueResult> IssueTokenAsync(Guid tenantId, Guid userId, DateTime expiresAtUtc, string? createdByIp = null, CancellationToken cancellationToken = default)
        {
            Issued++;
            return Task.FromResult(new RefreshTokenIssueResult("issued", Guid.NewGuid(), expiresAtUtc));
        }

        public Task<RefreshToken?> GetActiveTokenAsync(Guid tenantId, string token, CancellationToken cancellationToken = default) => Task.FromResult(Active);

        public Task<RefreshTokenIssueResult?> RotateTokenAsync(Guid tenantId, string token, DateTime newExpiresAtUtc, string? requestIp = null, CancellationToken cancellationToken = default) => Task.FromResult(RotateResult);

        public Task<bool> RevokeTokenAsync(Guid tenantId, string token, string? revokedByIp = null, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionInventoryItem>> GetActiveSessionsAsync(Guid tenantId, Guid userId, string? currentRefreshToken = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RevokeAllActiveTokensAsync(Guid tenantId, Guid userId, string? revokedByIp = null, string? reason = null, string? exceptRefreshToken = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RevokeAllTenantTokensAsync(Guid tenantId, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingNotificationService : IIdentityNotificationService
    {
        public bool Fail { get; set; }

        private Task Send() => Fail ? Task.FromException(new InvalidOperationException("mail relay down")) : Task.CompletedTask;

        public Task SendInviteAsync(Guid tenantId, string email, string inviteToken, DateTime expiresAt, CancellationToken cancellationToken = default) => Send();
        public Task SendVerificationAsync(Guid tenantId, string email, string verificationToken, DateTime expiresAt, CancellationToken cancellationToken = default) => Send();
        public Task SendPasswordResetAsync(Guid tenantId, string email, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default) => Send();
        public Task SendExistingAccountRegistrationNoticeAsync(string email, CancellationToken cancellationToken = default) => Send();
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task LogAsync(string action, string entityType, string entityId, object? changes = null, Guid? actorUserId = null) =>
            throw new InvalidOperationException("audit store unavailable");

        public Task<IReadOnlyCollection<TenantAuditLogItem>> GetTenantAuditLogsAsync(int page = 1, int pageSize = 50, string? action = null, DateTime? fromUtc = null, DateTime? toUtc = null) =>
            throw new NotSupportedException();
    }
}
