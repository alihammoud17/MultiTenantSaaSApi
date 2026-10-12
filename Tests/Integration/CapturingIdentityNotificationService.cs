using System.Collections.Concurrent;
using Domain.Interfaces;

namespace Tests.Integration;

/// <summary>
/// Stands in for email delivery in integration tests. Verification tokens are kept per tenant so helpers can
/// complete the register → verify → login flow with only an <see cref="HttpClient"/>.
/// Tenant ids are fresh GUIDs, so one store shared by every test factory cannot mix up tenants.
/// </summary>
public sealed class CapturingIdentityNotificationService : IIdentityNotificationService
{
    private static readonly ConcurrentDictionary<Guid, string> LatestVerificationTokens = new();

    public static string GetLatestVerificationToken(Guid tenantId)
    {
        return LatestVerificationTokens.TryGetValue(tenantId, out var token)
            ? token
            : throw new InvalidOperationException($"No verification token was sent for tenant {tenantId}.");
    }

    public Task SendInviteAsync(Guid tenantId, string email, string inviteToken, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SendVerificationAsync(Guid tenantId, string email, string verificationToken, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        LatestVerificationTokens[tenantId] = verificationToken;
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(Guid tenantId, string email, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
