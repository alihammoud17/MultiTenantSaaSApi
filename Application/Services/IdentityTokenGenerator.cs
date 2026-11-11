using System.Security.Cryptography;
using System.Text;

namespace Application.Services
{
    /// <summary>
    /// Opaque single-use identity tokens (invites, email verification, password reset).
    /// Only the hash is stored; the raw token is sent to the user.
    /// </summary>
    internal static class IdentityTokenGenerator
    {
        public static string GenerateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(48);
            return Convert.ToBase64String(bytes);
        }

        public static string ComputeHash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
