using Domain.DTOs;
using Domain.Responses;

namespace Domain.Outputs
{
    public enum AuthFlowError
    {
        None = 0,
        SubdomainAlreadyTaken,
        EmailAlreadyRegistered,
        RegistrationFailed,
        InvalidCredentials,
        EmailNotVerified,
        TenantSuspended,
        MfaChallengeRequired,
        InvalidOrExpiredRefreshToken,
        InvalidRefreshTokenContext,
        InvalidAuthenticatedContext,
        MfaAlreadyEnabled,
        InvalidOrExpiredEnrollmentChallenge,
        InvalidMfaCode,
        MfaNotEnrolled,
        StepUpRequired,
        InvalidOrExpiredStepUpToken
    }

    public sealed record RegisterAuthResult(
        bool Succeeded,
        AuthFlowError Error,
        AuthResponse? Response);

    public sealed record LoginAuthResult(
        bool Succeeded,
        AuthFlowError Error,
        AuthResponse? Response,
        bool RequiresMfa = false);

    public sealed record RefreshAuthResult(
        bool Succeeded,
        AuthFlowError Error,
        AuthResponse? Response);

    public sealed record InitiateMfaEnrollmentResult(
        bool Succeeded,
        AuthFlowError Error,
        string? EnrollmentToken,
        string? Secret,
        string? ProvisioningUri,
        DateTime? ExpiresAt);

    public sealed record CompleteMfaEnrollmentResult(
        bool Succeeded,
        AuthFlowError Error);

    public sealed record StepUpAuthenticationResult(
        bool Succeeded,
        AuthFlowError Error,
        string? StepUpToken,
        string? Purpose,
        DateTime? ExpiresAt);

    public sealed record StepUpValidationResult(
        bool IsValid,
        bool IsRequired,
        AuthFlowError Error);
}
