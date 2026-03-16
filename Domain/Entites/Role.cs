namespace Domain.Entites
{
    public class Role
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsSystem { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Tenant Tenant { get; set; } = null!;
        public List<RolePermission> RolePermissions { get; set; } = [];
        public List<UserRole> UserRoles { get; set; } = [];
    }
}
