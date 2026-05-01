using Domain.Entities;
using Domain.Interfaces;

namespace Infrastructure.Data
{
    public class RequestTenantResolutionCache : IRequestTenantResolutionCache
    {
        public Tenant? ResolvedTenant { get; private set; }

        public void SetResolvedTenant(Tenant tenant)
        {
            ResolvedTenant = tenant;
        }
    }
}
