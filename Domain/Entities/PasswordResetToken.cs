namespace Domain.Entities
{
    public class PasswordResetToken
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? RequestedByIp { get; set; }
        public DateTime? UsedAt { get; set; }

        public Tenant Tenant { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
