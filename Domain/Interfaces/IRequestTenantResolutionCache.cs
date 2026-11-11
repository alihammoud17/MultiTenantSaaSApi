using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IRequestTenantResolutionCache
    {
        Tenant? ResolvedTenant { get; }
        void SetResolvedTenant(Tenant tenant);
    }
}
