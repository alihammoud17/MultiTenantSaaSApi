using System.Security.Cryptography;
using System.Text;

namespace Application.Services;

public static class OutboundWebhookSigner
{
    public static string ComputeSignature(string signingSecret, string timestamp, string deliveryId, string payload)
    {
        var toSign = $"{timestamp}\n{deliveryId}\n{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        return "sha256=" + Convert.ToHexString(computedBytes).ToLowerInvariant();
    }
}
