using Application.Services;
using Domain.Entities;
using Domain.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class IdentityLifecycleServiceTests
{
    [Fact]
    public async Task CreateInviteAsync_ShouldNormalizeFields_AndSendInviteNotification()
    {
        var tenant = NewTenant();
        await using var db = CreateDbContext();
        await db.Tenants.AddAsync(tenant);
        await db.SaveChangesAsync();

        var notifications = new RecordingIdentityNotificationService();
        var sut = new IdentityLifecycleService(db, notifications);

        var result = await sut.CreateInviteAsync(tenant.Id, Guid.NewGuid(), "  user@example.com ", " member ", " Manager ", 24);

        result.InviteToken.Should().NotBeNullOrWhiteSpace();
        var invite = await db.UserInvites.SingleAsync();
        invite.Email.Should().Be("user@example.com");
        invite.Role.Should().Be("MEMBER");
        invite.RbacRoleName.Should().Be("Manager");
        invite.AcceptedAt.Should().BeNull();
        invite.TokenHash.Should().NotBe(result.InviteToken);

        notifications.Invites.Should().ContainSingle();
        notifications.Invites[0].TenantId.Should().Be(tenant.Id);
        notifications.Invites[0].Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task AcceptInviteAsync_ShouldCreateUserAssignRole_AndSendVerification()
    {
        var tenant = NewTenant();
        var actor = Guid.NewGuid();
        await using var db = CreateDbContext();
        await db.Tenants.AddAsync(tenant);
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "AdminOps", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await db.Roles.AddAsync(role);
        await db.SaveChangesAsync();

        var notifications = new RecordingIdentityNotificationService();
        var sut = new IdentityLifecycleService(db, notifications);

        var invite = await sut.CreateInviteAsync(tenant.Id, actor, "new.user@example.com", "member", role.Name, 48);

        var createdUserId = await sut.AcceptInviteAsync(tenant.Id, invite.InviteToken, "StrongPassword!123");

        createdUserId.Should().NotBeNull();
        var user = await db.Users.SingleAsync();
        user.Id.Should().Be(createdUserId!.Value);
        user.Email.Should().Be("new.user@example.com");
        user.Role.Should().Be("MEMBER");
        BCrypt.Net.BCrypt.Verify("StrongPassword!123", user.PasswordHash).Should().BeTrue();

        var inviteRecord = await db.UserInvites.SingleAsync();
        inviteRecord.AcceptedAt.Should().NotBeNull();

        var userRole = await db.UserRoles.SingleAsync();
        userRole.UserId.Should().Be(user.Id);
        userRole.RoleId.Should().Be(role.Id);

        (await db.UserVerificationTokens.CountAsync()).Should().Be(1);
        notifications.Verifications.Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptInviteAsync_ShouldReturnNull_ForWrongTenantTokenUsage()
    {
        var tenantA = NewTenant("a");
        var tenantB = NewTenant("b");
        await using var db = CreateDbContext();
        await db.Tenants.AddRangeAsync(tenantA, tenantB);
        await db.SaveChangesAsync();

        var notifications = new RecordingIdentityNotificationService();
        var sut = new IdentityLifecycleService(db, notifications);
        var invite = await sut.CreateInviteAsync(tenantA.Id, Guid.NewGuid(), "cross@tenant.com", "member", null, null);

        var accepted = await sut.AcceptInviteAsync(tenantB.Id, invite.InviteToken, "Password!123");

        accepted.Should().BeNull();
        (await db.Users.CountAsync()).Should().Be(0);
        (await db.UserInvites.SingleAsync()).AcceptedAt.Should().BeNull();
        notifications.Verifications.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteVerificationAsync_ShouldSucceedOnce_AndRejectReplay()
    {
        var tenant = NewTenant();
        await using var db = CreateDbContext();
        await db.Tenants.AddAsync(tenant);
        await db.Users.AddAsync(new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "verify@example.com", PasswordHash = "hash", Role = "MEMBER", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var notifications = new RecordingIdentityNotificationService();
        var sut = new IdentityLifecycleService(db, notifications);

        await sut.RequestVerificationAsync(tenant.Id, "verify@example.com");
        var token = notifications.Verifications.Single().Token;

        var first = await sut.CompleteVerificationAsync(tenant.Id, token);
        var second = await sut.CompleteVerificationAsync(tenant.Id, token);

        first.Should().BeTrue();
        second.Should().BeFalse();
        var user = await db.Users.SingleAsync();
        user.EmailVerifiedAt.Should().NotBeNull();
        var verificationRecord = await db.UserVerificationTokens.SingleAsync();
        verificationRecord.UsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CompletePasswordResetAsync_ShouldSucceedOnce_AndRotateStoredPasswordHash()
    {
        var tenant = NewTenant();
        await using var db = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "reset@example.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass!1"), Role = "MEMBER", CreatedAt = DateTime.UtcNow };
        await db.Tenants.AddAsync(tenant);
        await db.Users.AddAsync(user);
        await db.SaveChangesAsync();

        var notifications = new RecordingIdentityNotificationService();
        var sut = new IdentityLifecycleService(db, notifications);

        await sut.RequestPasswordResetAsync(tenant.Id, "reset@example.com", "127.0.0.1");
        var resetToken = notifications.PasswordResets.Single().Token;

        var first = await sut.CompletePasswordResetAsync(tenant.Id, resetToken, "NewPass!2");
        var second = await sut.CompletePasswordResetAsync(tenant.Id, resetToken, "AnotherPass!3");

        first.Should().BeTrue();
        second.Should().BeFalse();

        var updatedUser = await db.Users.SingleAsync();
        BCrypt.Net.BCrypt.Verify("NewPass!2", updatedUser.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("OldPass!1", updatedUser.PasswordHash).Should().BeFalse();
        (await db.PasswordResetTokens.SingleAsync()).UsedAt.Should().NotBeNull();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static Tenant NewTenant(string subdomain = "tenant") => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Tenant {subdomain}",
        Subdomain = subdomain,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class RecordingIdentityNotificationService : IIdentityNotificationService
    {
        public List<(Guid TenantId, string Email, string Token, DateTime ExpiresAt)> Invites { get; } = [];
        public List<(Guid TenantId, string Email, string Token, DateTime ExpiresAt)> Verifications { get; } = [];
        public List<(Guid TenantId, string Email, string Token, DateTime ExpiresAt)> PasswordResets { get; } = [];

        public Task SendInviteAsync(Guid tenantId, string email, string inviteToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            Invites.Add((tenantId, email, inviteToken, expiresAt));
            return Task.CompletedTask;
        }

        public Task SendVerificationAsync(Guid tenantId, string email, string verificationToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            Verifications.Add((tenantId, email, verificationToken, expiresAt));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(Guid tenantId, string email, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            PasswordResets.Add((tenantId, email, resetToken, expiresAt));
            return Task.CompletedTask;
        }
    }
}
