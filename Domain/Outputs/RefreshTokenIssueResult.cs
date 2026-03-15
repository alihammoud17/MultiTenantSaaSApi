namespace Domain.Outputs
{
    public sealed record RefreshTokenIssueResult(
        string Token,
        Guid RefreshTokenId,
        DateTime ExpiresAt);
}
