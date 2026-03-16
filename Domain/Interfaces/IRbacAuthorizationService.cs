namespace Domain.Interfaces
{
    public interface IRbacAuthorizationService
    {
        Task<bool> HasPermissionAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken cancellationToken = default);
        Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    }
}
