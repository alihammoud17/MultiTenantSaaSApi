namespace Domain.Interfaces
{
    public interface IMfaService
    {
        (string Secret, string ProvisioningUri) GenerateEnrollmentSecret(string issuer, string accountName);
        bool VerifyCode(string secret, string code);
        string GenerateOpaqueToken();
        string HashToken(string token);
    }
}
