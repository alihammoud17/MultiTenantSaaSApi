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
            var atIndex = sanitized.IndexOf('@');

            if (atIndex <= 0 || atIndex == sanitized.Length - 1)
            {
                return "***";
            }

            var localPart = sanitized[..atIndex];
            var domainPart = sanitized[(atIndex + 1)..];

            var visiblePrefix = localPart.Length <= 1 ? "*" : localPart[..1];
            return $"{visiblePrefix}***@{domainPart}";
        }

        public Task SendInviteAsync(Guid tenantId, string email, string inviteToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Identity invite notification queued. TenantId={TenantId}, Email={Email}, ExpiresAt={ExpiresAt}, TokenPreview={TokenPreview}",
                tenantId,
                MaskEmailForLog(email),
                expiresAt,
                SanitizeForLog(inviteToken[..Math.Min(8, inviteToken.Length)]));

            return Task.CompletedTask;
        }

        public Task SendVerificationAsync(Guid tenantId, string email, string verificationToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Verification notification queued. TenantId={TenantId}, Email={Email}, ExpiresAt={ExpiresAt}, TokenPreview={TokenPreview}",
                tenantId,
                MaskEmailForLog(email),
                expiresAt,
                SanitizeForLog(verificationToken[..Math.Min(8, verificationToken.Length)]));

            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(Guid tenantId, string email, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Password reset notification queued. TenantId={TenantId}, Email={Email}, ExpiresAt={ExpiresAt}, TokenPreview={TokenPreview}",
                tenantId,
                MaskEmailForLog(email),
                expiresAt,
                SanitizeForLog(resetToken[..Math.Min(8, resetToken.Length)]));

            return Task.CompletedTask;
        }
    }
}
