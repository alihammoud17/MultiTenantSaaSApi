namespace Domain.Entites
{
    public class RolePermission
    {
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }

        // Navigation
        public Role Role { get; set; } = null!;
        public Permission Permission { get; set; } = null!;
    }
}
