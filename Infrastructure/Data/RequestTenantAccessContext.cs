using Domain.Interfaces;

namespace Infrastructure.Data
{
    public class RequestTenantAccessContext : IRequestTenantAccessContext
    {
        public int? ApiCallsPerMonthLimit { get; private set; }

        public void SetApiCallsPerMonthLimit(int apiCallsPerMonthLimit)
        {
            ApiCallsPerMonthLimit = apiCallsPerMonthLimit;
        }
    }
}
