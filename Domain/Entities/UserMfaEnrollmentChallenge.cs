namespace Domain.Entities
{
    public class UserMfaEnrollmentChallenge
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string EnrollmentTokenHash { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime? ConsumedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
