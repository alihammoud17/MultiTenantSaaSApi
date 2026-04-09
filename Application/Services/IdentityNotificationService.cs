using Domain.Interfaces;

namespace Application.Services
{
    public class IdentityNotificationService : IIdentityNotificationService
    {
        private readonly ILogger<IdentityNotificationService> _logger;

        public IdentityNotificationService(ILogger<IdentityNotificationService> logger)
        {
            _logger = logger;
        }

        public Task SendInviteAsync(Guid tenantId, string email, string inviteToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Identity invite notification queued. TenantId={TenantId}, Email={Email}, ExpiresAt={ExpiresAt}, TokenPreview={TokenPreview}",
                tenantId,
                email,
                expiresAt,
                inviteToken[..Math.Min(8, inviteToken.Length)]);

            return Task.CompletedTask;
        }

        public Task SendVerificationAsync(Guid tenantId, string email, string verificationToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Verification notification queued. TenantId={TenantId}, Email={Email}, ExpiresAt={ExpiresAt}, TokenPreview={TokenPreview}",
                tenantId,
                email,
                expiresAt,
                verificationToken[..Math.Min(8, verificationToken.Length)]);

            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(Guid tenantId, string email, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Password reset notification queued. TenantId={TenantId}, Email={Email}, ExpiresAt={ExpiresAt}, TokenPreview={TokenPreview}",
                tenantId,
                email,
                expiresAt,
                resetToken[..Math.Min(8, resetToken.Length)]);

            return Task.CompletedTask;
        }
    }
}
