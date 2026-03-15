namespace Domain.Responses
{
    public record AuthResponse(
        string Token,
        string RefreshToken,
        Guid TenantId,
        Guid UserId,
        string Email,
        DateTime ExpiresAt,
        DateTime RefreshTokenExpiresAt
    );
}
