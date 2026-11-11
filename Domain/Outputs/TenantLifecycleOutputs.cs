using Domain.Entities;

namespace Domain.Outputs
{
    public enum TenantStatusChangeOutcome
    {
        Changed = 0,
        TenantNotFound,
        InvalidTransition,
        StaleRequest
    }

    public sealed record TenantStatusChangeResult(TenantStatusChangeOutcome Outcome, TenantStatus? Status);
}
