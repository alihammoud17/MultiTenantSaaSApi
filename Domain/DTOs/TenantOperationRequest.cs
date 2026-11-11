namespace Domain.DTOs
{
    // Body of the signed internal tenant-operations requests. Actor is a free-text label for the audit log.
    public record TenantOperationRequest(
        string? Reason,
        string? Actor
    );
}
