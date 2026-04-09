namespace Domain.Outputs
{
    public record CreatedInviteResult(
        Guid InviteId,
        DateTime ExpiresAt,
        string InviteToken
    );

    public record SessionInventoryItem(
        Guid SessionId,
        DateTime CreatedAt,
        DateTime ExpiresAt,
        string? CreatedByIp,
        bool IsCurrentSession
    );

    public record SessionRevokeAllResult(
        int RevokedCount,
        Guid EffectiveUserId
    );
}
