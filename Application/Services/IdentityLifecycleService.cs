using Domain.Entites;
using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services
{
    public class IdentityLifecycleService : IIdentityLifecycleService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IIdentityNotificationService _notificationService;

        public IdentityLifecycleService(
            ApplicationDbContext dbContext,
            IIdentityNotificationService notificationService)
        {
            _dbContext = dbContext;
            _notificationService = notificationService;
        }

        public async Task<CreatedInviteResult> CreateInviteAsync(Guid tenantId, Guid actorUserId, string email, string? role, string? rbacRoleName, int? expiresInHours, CancellationToken cancellationToken = default)
        {
            if (await _dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken))
            {
                throw new InvalidOperationException("User already exists for tenant.");
            }

            var token = GenerateToken();
            var invite = new UserInvite
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = email.Trim(),
                Role = string.IsNullOrWhiteSpace(role) ? "MEMBER" : role.Trim().ToUpperInvariant(),
                RbacRoleName = string.IsNullOrWhiteSpace(rbacRoleName) ? null : rbacRoleName.Trim(),
                TokenHash = ComputeHash(token),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(expiresInHours.GetValueOrDefault(72)),
                CreatedByUserId = actorUserId
            };

            await _dbContext.UserInvites.AddAsync(invite, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _notificationService.SendInviteAsync(tenantId, invite.Email, token, invite.ExpiresAt, cancellationToken);

            return new CreatedInviteResult(invite.Id, invite.ExpiresAt, token);
        }

        public async Task<Guid?> AcceptInviteAsync(Guid tenantId, string inviteToken, string password, CancellationToken cancellationToken = default)
        {
            var tokenHash = ComputeHash(inviteToken);
            var invite = await _dbContext.UserInvites
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.TokenHash == tokenHash && i.AcceptedAt == null && i.ExpiresAt > DateTime.UtcNow, cancellationToken);

            if (invite == null)
            {
                return null;
            }

            if (await _dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Email == invite.Email, cancellationToken))
            {
                return null;
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = invite.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = invite.Role,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddAsync(user, cancellationToken);

            if (!string.IsNullOrWhiteSpace(invite.RbacRoleName))
            {
                var role = await _dbContext.Roles
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == invite.RbacRoleName, cancellationToken);

                if (role != null)
                {
                    await _dbContext.UserRoles.AddAsync(new UserRole
                    {
                        TenantId = tenantId,
                        UserId = user.Id,
                        RoleId = role.Id,
                        AssignedAt = DateTime.UtcNow
                    }, cancellationToken);
                }
            }

            var verificationToken = GenerateToken();
            var verificationExpiresAt = DateTime.UtcNow.AddHours(48);
            await _dbContext.UserVerificationTokens.AddAsync(new UserVerificationToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                TokenHash = ComputeHash(verificationToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = verificationExpiresAt
            }, cancellationToken);

            invite.AcceptedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _notificationService.SendVerificationAsync(tenantId, user.Email, verificationToken, verificationExpiresAt, cancellationToken);

            return user.Id;
        }

        public async Task RequestVerificationAsync(Guid tenantId, string email, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email.Trim(), cancellationToken);

            if (user == null || user.EmailVerifiedAt.HasValue)
            {
                return;
            }

            var token = GenerateToken();
            var expiresAt = DateTime.UtcNow.AddHours(24);
            await _dbContext.UserVerificationTokens.AddAsync(new UserVerificationToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                TokenHash = ComputeHash(token),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _notificationService.SendVerificationAsync(tenantId, user.Email, token, expiresAt, cancellationToken);
        }

        public async Task<bool> CompleteVerificationAsync(Guid tenantId, string verificationToken, CancellationToken cancellationToken = default)
        {
            var tokenHash = ComputeHash(verificationToken);
            var record = await _dbContext.UserVerificationTokens
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > DateTime.UtcNow, cancellationToken);

            if (record == null)
            {
                return false;
            }

            record.UsedAt = DateTime.UtcNow;
            record.User.EmailVerifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task RequestPasswordResetAsync(Guid tenantId, string email, string? requestIp, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email.Trim(), cancellationToken);

            if (user == null)
            {
                return;
            }

            var token = GenerateToken();
            var expiresAt = DateTime.UtcNow.AddHours(2);

            await _dbContext.PasswordResetTokens.AddAsync(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                TokenHash = ComputeHash(token),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                RequestedByIp = requestIp
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _notificationService.SendPasswordResetAsync(tenantId, user.Email, token, expiresAt, cancellationToken);
        }

        public async Task<bool> CompletePasswordResetAsync(Guid tenantId, string resetToken, string newPassword, CancellationToken cancellationToken = default)
        {
            var tokenHash = ComputeHash(resetToken);
            var record = await _dbContext.PasswordResetTokens
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > DateTime.UtcNow, cancellationToken);

            if (record == null)
            {
                return false;
            }

            record.UsedAt = DateTime.UtcNow;
            record.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        private static string GenerateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(48);
            return Convert.ToBase64String(bytes);
        }

        private static string ComputeHash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
