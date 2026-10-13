using BCrypt.Net;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
            var normalizedEmail = email.Trim();
            if (await _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
            {
                throw new InvalidOperationException("User already exists.");
            }

            var token = GenerateToken();
            var invite = new UserInvite
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = normalizedEmail,
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

            if (invite == null || !await IsTenantActiveAsync(tenantId, cancellationToken))
            {
                return null;
            }

            if (await _dbContext.Users.AnyAsync(u => u.Email == invite.Email, cancellationToken))
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
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email.Trim(), cancellationToken);

            if (user == null || user.EmailVerifiedAt.HasValue || !AllowsVerification(user.Tenant.Status))
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
                .Include(x => x.Tenant)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > DateTime.UtcNow, cancellationToken);

            // A suspended tenant must not be able to complete identity flows; verification never changes that status.
            if (record == null || !AllowsVerification(record.Tenant.Status))
            {
                return false;
            }

            var now = DateTime.UtcNow;
            record.UsedAt = now;
            record.User.EmailVerifiedAt = now;

            // A self-registered tenant becomes usable once its admin proves they own the email address.
            if (record.Tenant.Status == TenantStatus.PendingVerification)
            {
                record.Tenant.Status = TenantStatus.Active;
                record.Tenant.UpdatedAt = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task RequestPasswordResetAsync(Guid tenantId, string email, string? requestIp, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email.Trim(), cancellationToken);

            if (user == null || user.Tenant.Status != TenantStatus.Active)
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
                .Include(x => x.Tenant)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > DateTime.UtcNow, cancellationToken);

            if (record == null || record.Tenant.Status != TenantStatus.Active)
            {
                return false;
            }

            record.UsedAt = DateTime.UtcNow;
            record.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Email verification also serves self-registered tenants that are waiting to be activated.
        private static bool AllowsVerification(TenantStatus status) =>
            status is TenantStatus.Active or TenantStatus.PendingVerification;

        private Task<bool> IsTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            return _dbContext.Tenants.AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, cancellationToken);
        }

        private static string GenerateToken() => IdentityTokenGenerator.GenerateToken();

        private static string ComputeHash(string token) => IdentityTokenGenerator.ComputeHash(token);
    }
}
