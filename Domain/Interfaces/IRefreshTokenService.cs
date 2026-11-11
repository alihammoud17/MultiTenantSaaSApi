using Domain.Entities;
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

        Task<IReadOnlyList<SessionInventoryItem>> GetActiveSessionsAsync(
            Guid tenantId,
            Guid userId,
            string? currentRefreshToken = null,
            CancellationToken cancellationToken = default);

        Task<int> RevokeAllActiveTokensAsync(
            Guid tenantId,
            Guid userId,
            string? revokedByIp = null,
            string? reason = null,
            string? exceptRefreshToken = null,
            CancellationToken cancellationToken = default);

        // Revokes every active refresh token of every user in the tenant (e.g. on suspension).
        // RefreshTokens are tenant-filtered, so the caller must have set the tenant context to tenantId.
        Task<int> RevokeAllTenantTokensAsync(
            Guid tenantId,
            string? reason = null,
            CancellationToken cancellationToken = default);
    }
}
