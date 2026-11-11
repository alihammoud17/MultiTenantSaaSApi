namespace Domain.Outputs
{
    // A signed JWT and the exact expiry written into its exp claim.
    public sealed record AccessToken(string Token, DateTime ExpiresAt);
}
