using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Application.Services;

public sealed class InternalRequestSignatureValidator : IInternalRequestSignatureValidator
{
    private const string SignaturePrefix = "sha256=";
    private readonly string _sharedSecret;
    private readonly TimeSpan _allowedClockSkew;

    public InternalRequestSignatureValidator(IConfiguration configuration)
    {
        _sharedSecret = configuration["BillingIntegration:SharedSecret"] ?? string.Empty;
        var allowedClockSkewMinutes = int.TryParse(configuration["BillingIntegration:AllowedClockSkewMinutes"], out var parsedClockSkewMinutes)
            ? parsedClockSkewMinutes
            : 5;
        _allowedClockSkew = TimeSpan.FromMinutes(allowedClockSkewMinutes);
    }

    public bool IsSignatureValid(string payload, string? timestamp, string? signature)
    {
        if (string.IsNullOrWhiteSpace(_sharedSecret) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedTimestamp))
        {
            return false;
        }

        var utcNow = DateTimeOffset.UtcNow;
        if (utcNow - parsedTimestamp > _allowedClockSkew || parsedTimestamp - utcNow > _allowedClockSkew)
        {
            return false;
        }

        var payloadToSign = $"{timestamp}\n{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_sharedSecret));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSign));
        var computedSignature = SignaturePrefix + Convert.ToHexString(computedBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature));
    }
}
