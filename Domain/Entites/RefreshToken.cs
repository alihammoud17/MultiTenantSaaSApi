namespace Domain.Entites
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByIp { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokedByIp { get; set; }
        public string? RevocationReason { get; set; }
        public Guid? ReplacedByTokenId { get; set; }

        public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;

        public Tenant Tenant { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
