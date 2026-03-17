namespace Domain.DTOs
{
    public record InviteTenantUserRequest(
        string Email,
        string Password,
        string? Role = null,
        string? RbacRoleName = null
    );

    public record ChangeTenantUserRoleRequest(
        string Role,
        string? RbacRoleName = null
    );
}
