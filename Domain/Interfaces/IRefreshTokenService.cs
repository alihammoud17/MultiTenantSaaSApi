using Domain.Entites;
using Domain.Outputs;

namespace Domain.Interfaces
{
    public interface IRefreshTokenService
    {
        Task<RefreshTokenIssueResult> IssueTokenAsync(
            Guid tenantId,
            Guid userId,
            DateTime expiresAtUtc,
            string? createdByIp = null,
            CancellationToken cancellationToken = default);

        Task<RefreshToken?> GetActiveTokenAsync(
            Guid tenantId,
            string token,
            CancellationToken cancellationToken = default);

        Task<bool> RevokeTokenAsync(
            Guid tenantId,
            string token,
            string? revokedByIp = null,
            string? reason = null,
            CancellationToken cancellationToken = default);

        Task<RefreshTokenIssueResult?> RotateTokenAsync(
            Guid tenantId,
            string token,
            DateTime newExpiresAtUtc,
            string? requestIp = null,
            CancellationToken cancellationToken = default);
    }
}
