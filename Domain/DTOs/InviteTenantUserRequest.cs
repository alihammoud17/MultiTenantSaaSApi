namespace Domain.DTOs
{
    public class InviteTenantUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "MEMBER";
    }
}
