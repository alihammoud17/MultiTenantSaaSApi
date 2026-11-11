namespace Domain.Responses
{
    public record RegistrationPendingResponse(
        Guid TenantId,
        string Status,
        DateTime VerificationExpiresAt
    )
    {
        public const string PendingVerificationStatus = "pending_verification";
    }
}
