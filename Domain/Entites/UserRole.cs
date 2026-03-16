namespace Domain.Entites
{
    public class UserRole
    {
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
        public DateTime AssignedAt { get; set; }

        // Navigation
        public Tenant Tenant { get; set; } = null!;
        public User User { get; set; } = null!;
        public Role Role { get; set; } = null!;
    }
}
