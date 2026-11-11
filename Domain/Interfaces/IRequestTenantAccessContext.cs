namespace Domain.Interfaces
{
    public interface IRequestTenantAccessContext
    {
        int? ApiCallsPerMonthLimit { get; }
        void SetApiCallsPerMonthLimit(int apiCallsPerMonthLimit);
    }
}
