namespace Domain.Entites
{
    public class UserInvite
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "MEMBER";
        public string? RbacRoleName { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedByUserId { get; set; }
        public DateTime? AcceptedAt { get; set; }

        public Tenant Tenant { get; set; } = null!;
    }
}
