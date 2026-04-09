using Domain.Outputs;

namespace Domain.Interfaces
{
    public interface IIdentityLifecycleService
    {
        Task<CreatedInviteResult> CreateInviteAsync(
            Guid tenantId,
            Guid actorUserId,
            string email,
            string? role,
            string? rbacRoleName,
            int? expiresInHours,
            CancellationToken cancellationToken = default);

        Task<Guid?> AcceptInviteAsync(
            Guid tenantId,
            string inviteToken,
            string password,
            CancellationToken cancellationToken = default);

        Task RequestVerificationAsync(
            Guid tenantId,
            string email,
            CancellationToken cancellationToken = default);

        Task<bool> CompleteVerificationAsync(
            Guid tenantId,
            string verificationToken,
            CancellationToken cancellationToken = default);

        Task RequestPasswordResetAsync(
            Guid tenantId,
            string email,
            string? requestIp,
            CancellationToken cancellationToken = default);

        Task<bool> CompletePasswordResetAsync(
            Guid tenantId,
            string resetToken,
            string newPassword,
            CancellationToken cancellationToken = default);
    }
}
