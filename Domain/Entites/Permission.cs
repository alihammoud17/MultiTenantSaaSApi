namespace Domain.Entites
{
    public class Permission
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        // Navigation
        public List<RolePermission> RolePermissions { get; set; } = [];
    }
}
