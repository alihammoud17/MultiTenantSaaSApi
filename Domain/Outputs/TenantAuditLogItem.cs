namespace Domain.Outputs
{
    public sealed record TenantAuditLogItem(
        Guid Id,
        Guid TenantId,
        Guid UserId,
        string Action,
        string EntityType,
        string EntityId,
        string? Changes,
        DateTime Timestamp,
        string IpAddress);
}
