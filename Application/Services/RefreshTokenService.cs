using Domain.Entites;
using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly ApplicationDbContext _dbContext;

        public RefreshTokenService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<RefreshTokenIssueResult> IssueTokenAsync(
            Guid tenantId,
            Guid userId,
            DateTime expiresAtUtc,
            string? createdByIp = null,
            CancellationToken cancellationToken = default)
        {
            var userExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken);

            if (!userExists)
            {
                throw new InvalidOperationException("User does not exist in the provided tenant.");
            }

            var token = GenerateToken();
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                TokenHash = ComputeHash(token),
                ExpiresAt = expiresAtUtc,
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = createdByIp
            };

            await _dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new RefreshTokenIssueResult(token, refreshToken.Id, refreshToken.ExpiresAt);
        }

        public async Task<RefreshToken?> GetActiveTokenAsync(
            Guid tenantId,
            string token,
            CancellationToken cancellationToken = default)
        {
            var tokenHash = ComputeHash(token);

            return await _dbContext.RefreshTokens
                .SingleOrDefaultAsync(
                    t => t.TenantId == tenantId
                        && t.TokenHash == tokenHash
                        && t.RevokedAt == null
                        && t.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);
        }

        public async Task<bool> RevokeTokenAsync(
            Guid tenantId,
            string token,
            string? revokedByIp = null,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            var refreshToken = await GetActiveTokenAsync(tenantId, token, cancellationToken);

            if (refreshToken == null)
            {
                return false;
            }

            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.RevokedByIp = revokedByIp;
            refreshToken.RevocationReason = reason;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<RefreshTokenIssueResult?> RotateTokenAsync(
            Guid tenantId,
            string token,
            DateTime newExpiresAtUtc,
            string? requestIp = null,
            CancellationToken cancellationToken = default)
        {
            var currentToken = await GetActiveTokenAsync(tenantId, token, cancellationToken);
            if (currentToken == null)
            {
                return null;
            }

            var newTokenValue = GenerateToken();
            var newToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = currentToken.TenantId,
                UserId = currentToken.UserId,
                TokenHash = ComputeHash(newTokenValue),
                ExpiresAt = newExpiresAtUtc,
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = requestIp
            };

            currentToken.RevokedAt = DateTime.UtcNow;
            currentToken.RevokedByIp = requestIp;
            currentToken.RevocationReason = "ROTATED";
            currentToken.ReplacedByTokenId = newToken.Id;

            await _dbContext.RefreshTokens.AddAsync(newToken, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new RefreshTokenIssueResult(newTokenValue, newToken.Id, newToken.ExpiresAt);
        }

        public async Task<IReadOnlyList<SessionInventoryItem>> GetActiveSessionsAsync(
            Guid tenantId,
            Guid userId,
            string? currentRefreshToken = null,
            CancellationToken cancellationToken = default)
        {
            string? currentTokenHash = string.IsNullOrWhiteSpace(currentRefreshToken)
                ? null
                : ComputeHash(currentRefreshToken);

            var sessions = await _dbContext.RefreshTokens
                .AsNoTracking()
                .Where(t => t.TenantId == tenantId
                    && t.UserId == userId
                    && t.RevokedAt == null
                    && t.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new SessionInventoryItem(
                    t.Id,
                    t.CreatedAt,
                    t.ExpiresAt,
                    t.CreatedByIp,
                    currentTokenHash != null && t.TokenHash == currentTokenHash))
                .ToListAsync(cancellationToken);

            return sessions;
        }

        public async Task<int> RevokeAllActiveTokensAsync(
            Guid tenantId,
            Guid userId,
            string? revokedByIp = null,
            string? reason = null,
            string? exceptRefreshToken = null,
            CancellationToken cancellationToken = default)
        {
            string? exceptHash = string.IsNullOrWhiteSpace(exceptRefreshToken)
                ? null
                : ComputeHash(exceptRefreshToken);

            var activeTokens = await _dbContext.RefreshTokens
                .Where(t => t.TenantId == tenantId
                    && t.UserId == userId
                    && t.RevokedAt == null
                    && t.ExpiresAt > DateTime.UtcNow
                    && (exceptHash == null || t.TokenHash != exceptHash))
                .ToListAsync(cancellationToken);

            foreach (var token in activeTokens)
            {
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedByIp = revokedByIp;
                token.RevocationReason = reason;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return activeTokens.Count;
        }

        private static string GenerateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes);
        }

        private static string ComputeHash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
