namespace Domain.Interfaces
{
    public interface IRbacAuthorizationService
    {
        Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, string permission, CancellationToken cancellationToken = default);
    }
}
