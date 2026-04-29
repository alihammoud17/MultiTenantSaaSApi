namespace Domain.Entities
{
    public class UserStepUpSession
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string SessionTokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
