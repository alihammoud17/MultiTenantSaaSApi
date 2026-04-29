using System.Text.Json;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Outputs;
using Domain.Responses;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Presentation.Controllers;
using Presentation.RateLimiting;
using System.Reflection;

namespace Tests.UnitTests;

public class AuthControllerBoundaryTests
{
    [Theory]
    [InlineData(nameof(AuthController.Register))]
    [InlineData(nameof(AuthController.Login))]
    [InlineData(nameof(AuthController.Refresh))]
    public void HighRiskUnauthenticatedEndpoints_ShouldUseAuthBruteForceRateLimitPolicy(string methodName)
    {
        var method = typeof(AuthController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        attribute.Should().NotBeNull();
        attribute!.PolicyName.Should().Be(AuthRateLimitPolicyNames.UnauthenticatedAuthEndpoints);
    }

    [Fact]
    public async Task Register_ShouldMapSubdomainConflict_ToExistingBadRequestContract()
    {
        var service = new RecordingAuthOrchestrationService
        {
            RegisterResult = new RegisterAuthResult(false, AuthFlowError.SubdomainAlreadyTaken, null)
        };
        var sut = CreateController(service, "203.0.113.11");

        var result = await sut.Register(new RegisterTenantRequest("Acme", "acme", "admin@example.com", "Passw0rd!"), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        GetPayloadProperty(badRequest.Value, "error").Should().Be("Subdomain already taken");
        service.RegisterCallCount.Should().Be(1);
        service.LastRegisterIp.Should().Be("203.0.113.11");
    }

    [Fact]
    public async Task Register_ShouldMapSuccess_ToOkWithAuthResponse()
    {
        var response = new AuthResponse("jwt-token", "refresh-token", Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", DateTime.UtcNow.AddMinutes(15), DateTime.UtcNow.AddDays(7));
        var service = new RecordingAuthOrchestrationService
        {
            RegisterResult = new RegisterAuthResult(true, AuthFlowError.None, response)
        };
        var sut = CreateController(service, "198.51.100.12");

        var result = await sut.Register(new RegisterTenantRequest("Acme", "acme", "admin@example.com", "Passw0rd!"), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(response);
        service.RegisterCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Login_ShouldMapMfaChallenge_ToUnauthorizedWithRequiresMfaFlag()
    {
        var service = new RecordingAuthOrchestrationService
        {
            LoginResult = new LoginAuthResult(false, AuthFlowError.MfaChallengeRequired, null, RequiresMfa: true)
        };
        var sut = CreateController(service, "192.0.2.33");

        var result = await sut.Login(new LoginRequest("admin@example.com", "Passw0rd!"), CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        GetPayloadProperty(unauthorized.Value, "error").Should().Be("MFA challenge required");
        GetPayloadProperty(unauthorized.Value, "requiresMfa").Should().Be("True");
        service.LoginCallCount.Should().Be(1);
        service.LastLoginIp.Should().Be("192.0.2.33");
    }

    [Fact]
    public async Task Login_ShouldMapSuspendedTenant_ToForbiddenContract()
    {
        var service = new RecordingAuthOrchestrationService
        {
            LoginResult = new LoginAuthResult(false, AuthFlowError.TenantSuspended, null)
        };
        var sut = CreateController(service, null);

        var result = await sut.Login(new LoginRequest("admin@example.com", "Passw0rd!"), CancellationToken.None);

        var forbidden = result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        GetPayloadProperty(forbidden.Value, "error").Should().Be("Tenant account is suspended");
    }

    [Fact]
    public async Task Refresh_ShouldShortCircuit_WhenTenantIdMissing_WithoutCallingService()
    {
        var service = new RecordingAuthOrchestrationService();
        var sut = CreateController(service, "203.0.113.44");

        var result = await sut.Refresh(new RefreshTokenRequest(Guid.Empty, "refresh-token"), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        GetPayloadProperty(badRequest.Value, "error").Should().Be("TenantId is required");
        service.RefreshCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_ShouldShortCircuit_WhenRefreshTokenMissing_WithoutCallingService()
    {
        var service = new RecordingAuthOrchestrationService();
        var sut = CreateController(service, "203.0.113.55");

        var result = await sut.Refresh(new RefreshTokenRequest(Guid.NewGuid(), string.Empty), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        GetPayloadProperty(badRequest.Value, "error").Should().Be("RefreshToken is required");
        service.RefreshCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_ShouldMapInvalidContext_ToUnauthorizedContract()
    {
        var service = new RecordingAuthOrchestrationService
        {
            RefreshResult = new RefreshAuthResult(false, AuthFlowError.InvalidRefreshTokenContext, null)
        };
        var sut = CreateController(service, "198.51.100.88");
        var tenantId = Guid.NewGuid();

        var result = await sut.Refresh(new RefreshTokenRequest(tenantId, "refresh-token"), CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        GetPayloadProperty(unauthorized.Value, "error").Should().Be("Invalid refresh token context");
        service.RefreshCallCount.Should().Be(1);
        service.LastRefreshTenantId.Should().Be(tenantId);
        service.LastRefreshToken.Should().Be("refresh-token");
        service.LastRefreshIp.Should().Be("198.51.100.88");
    }

    private static AuthController CreateController(RecordingAuthOrchestrationService service, string? remoteIp)
    {
        var controller = new AuthController(
            service,
            new NoOpAuditService(),
            new NoOpRefreshTokenService(),
            new StubTenantContext(),
            new NoOpIdentityLifecycleService());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            controller.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        }

        return controller;
    }

    private static string? GetPayloadProperty(object? payload, string propertyName)
    {
        payload.Should().NotBeNull();
        var json = JsonSerializer.Serialize(payload);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return element.GetProperty(propertyName).ToString();
    }

    private sealed class RecordingAuthOrchestrationService : IAuthOrchestrationService
    {
        public RegisterAuthResult RegisterResult { get; set; } = new(false, AuthFlowError.RegistrationFailed, null);
        public LoginAuthResult LoginResult { get; set; } = new(false, AuthFlowError.InvalidCredentials, null);
        public RefreshAuthResult RefreshResult { get; set; } = new(false, AuthFlowError.InvalidOrExpiredRefreshToken, null);

        public int RegisterCallCount { get; private set; }
        public int LoginCallCount { get; private set; }
        public int RefreshCallCount { get; private set; }

        public string? LastRegisterIp { get; private set; }
        public string? LastLoginIp { get; private set; }
        public Guid LastRefreshTenantId { get; private set; }
        public string? LastRefreshToken { get; private set; }
        public string? LastRefreshIp { get; private set; }

        public Task<RegisterAuthResult> RegisterAsync(RegisterTenantRequest request, string? requestIp, CancellationToken cancellationToken = default)
        {
            RegisterCallCount++;
            LastRegisterIp = requestIp;
            return Task.FromResult(RegisterResult);
        }

        public Task<LoginAuthResult> LoginAsync(LoginRequest request, string? requestIp, CancellationToken cancellationToken = default)
        {
            LoginCallCount++;
            LastLoginIp = requestIp;
            return Task.FromResult(LoginResult);
        }

        public Task<RefreshAuthResult> RefreshAsync(Guid tenantId, string refreshToken, string? requestIp, CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            LastRefreshTenantId = tenantId;
            LastRefreshToken = refreshToken;
            LastRefreshIp = requestIp;
            return Task.FromResult(RefreshResult);
        }

        public Task<InitiateMfaEnrollmentResult> InitiateMfaEnrollmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CompleteMfaEnrollmentResult> CompleteMfaEnrollmentAsync(Guid tenantId, Guid userId, string enrollmentToken, string code, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StepUpAuthenticationResult> StepUpAuthenticationAsync(Guid tenantId, Guid userId, string code, string? purpose, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StepUpValidationResult> ValidateStepUpAsync(Guid tenantId, Guid userId, string purpose, string? stepUpToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(string action, string entityType, string entityId, object? changes = null) => Task.CompletedTask;

        public Task<IReadOnlyCollection<TenantAuditLogItem>> GetTenantAuditLogsAsync(int page = 1, int pageSize = 50, string? action = null, DateTime? fromUtc = null, DateTime? toUtc = null)
            => Task.FromResult<IReadOnlyCollection<TenantAuditLogItem>>(Array.Empty<TenantAuditLogItem>());
    }

    private sealed class NoOpRefreshTokenService : IRefreshTokenService
    {
        public Task<RefreshTokenIssueResult> IssueTokenAsync(Guid tenantId, Guid userId, DateTime expiresAtUtc, string? createdByIp = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Domain.Entities.RefreshToken?> GetActiveTokenAsync(Guid tenantId, string token, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RevokeTokenAsync(Guid tenantId, string token, string? revokedByIp = null, string? reason = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RefreshTokenIssueResult?> RotateTokenAsync(Guid tenantId, string token, DateTime newExpiresAtUtc, string? requestIp = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SessionInventoryItem>> GetActiveSessionsAsync(Guid tenantId, Guid userId, string? currentRefreshToken = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> RevokeAllActiveTokensAsync(Guid tenantId, Guid userId, string? revokedByIp = null, string? reason = null, string? exceptRefreshToken = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }

        public void SetTenantId(Guid tenantId)
        {
            TenantId = tenantId;
        }
    }

    private sealed class NoOpIdentityLifecycleService : IIdentityLifecycleService
    {
        public Task<CreatedInviteResult> CreateInviteAsync(Guid tenantId, Guid actorUserId, string email, string? role, string? rbacRoleName, int? expiresInHours, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Guid?> AcceptInviteAsync(Guid tenantId, string inviteToken, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RequestVerificationAsync(Guid tenantId, string email, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> CompleteVerificationAsync(Guid tenantId, string verificationToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RequestPasswordResetAsync(Guid tenantId, string email, string? requestIp, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> CompletePasswordResetAsync(Guid tenantId, string resetToken, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
