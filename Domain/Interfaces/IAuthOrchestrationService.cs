using Domain.DTOs;
using Domain.Outputs;

namespace Domain.Interfaces
{
    public interface IAuthOrchestrationService
    {
        Task<RegisterAuthResult> RegisterAsync(
            RegisterTenantRequest request,
            string? requestIp,
            CancellationToken cancellationToken = default);

        Task<LoginAuthResult> LoginAsync(
            LoginRequest request,
            string? requestIp,
            CancellationToken cancellationToken = default);

        Task<RefreshAuthResult> RefreshAsync(
            Guid tenantId,
            string refreshToken,
            string? requestIp,
            CancellationToken cancellationToken = default);

        Task<InitiateMfaEnrollmentResult> InitiateMfaEnrollmentAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<CompleteMfaEnrollmentResult> CompleteMfaEnrollmentAsync(
            Guid tenantId,
            Guid userId,
            string enrollmentToken,
            string code,
            CancellationToken cancellationToken = default);

        Task<StepUpAuthenticationResult> StepUpAuthenticationAsync(
            Guid tenantId,
            Guid userId,
            string code,
            string? purpose,
            CancellationToken cancellationToken = default);

        Task<StepUpValidationResult> ValidateStepUpAsync(
            Guid tenantId,
            Guid userId,
            string purpose,
            string? stepUpToken,
            CancellationToken cancellationToken = default);
    }
}
