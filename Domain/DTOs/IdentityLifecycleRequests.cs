namespace Domain.DTOs
{
    public record CreateInviteRequest(
        string Email,
        string? Role,
        string? RbacRoleName,
        int? ExpiresInHours = null
    );

    public record AcceptInviteRequest(
        Guid TenantId,
        string InviteToken,
        string Password
    );

    public record RequestVerificationRequest(
        Guid TenantId,
        string Email
    );

    public record CompleteVerificationRequest(
        Guid TenantId,
        string VerificationToken
    );

    public record RequestPasswordResetRequest(
        Guid TenantId,
        string Email
    );

    public record CompletePasswordResetRequest(
        Guid TenantId,
        string ResetToken,
        string NewPassword
    );

    public record RevokeAllSessionsRequest(
        Guid TenantId,
        Guid? UserId,
        string? Reason = null
    );

    public record CompleteMfaEnrollmentRequest(
        string EnrollmentToken,
        string Code
    );

    public record StepUpAuthenticationRequest(
        string Code,
        string? Purpose = null
    );
}
