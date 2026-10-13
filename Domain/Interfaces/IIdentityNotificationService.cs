namespace Domain.Interfaces
{
    public interface IIdentityNotificationService
    {
        Task SendInviteAsync(Guid tenantId, string email, string inviteToken, DateTime expiresAt, CancellationToken cancellationToken = default);
        Task SendVerificationAsync(Guid tenantId, string email, string verificationToken, DateTime expiresAt, CancellationToken cancellationToken = default);
        Task SendPasswordResetAsync(Guid tenantId, string email, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default);
        // Someone tried to register a new tenant with an email that already has an account.
        Task SendExistingAccountRegistrationNoticeAsync(string email, CancellationToken cancellationToken = default);
    }
}
