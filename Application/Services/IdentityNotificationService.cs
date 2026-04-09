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

        private static string SanitizeForLog(string value)
        {
            return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        private static string MaskEmailForLog(string email)
        {
            var sanitized = SanitizeForLog(email).Trim();

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return "[REDACTED_EMAIL]";
            }

            return "[REDACTED_EMAIL]";
        }

        public Task SendInviteAsync(Guid tenantId, string email, string inviteToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Identity invite notification queued. TenantId={TenantId}, ExpiresAt={ExpiresAt}",
                tenantId,
                expiresAt);

            return Task.CompletedTask;
        }

        public Task SendVerificationAsync(Guid tenantId, string email, string verificationToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Verification notification queued. TenantId={TenantId}, ExpiresAt={ExpiresAt}",
                tenantId,
                expiresAt);

            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(Guid tenantId, string email, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Password reset notification queued. TenantId={TenantId}, ExpiresAt={ExpiresAt}",
                tenantId,
                expiresAt);

            return Task.CompletedTask;
        }
    }
}
