namespace Domain.Entities
{
    public class UserVerificationToken
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UsedAt { get; set; }

        public Tenant Tenant { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
