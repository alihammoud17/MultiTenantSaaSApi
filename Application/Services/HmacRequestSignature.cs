using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services;

/// <summary>
/// Shared check for signed internal requests: <c>sha256=</c> + hex HMAC-SHA256 of <c>"{timestamp}\n{payload}"</c>,
/// with the timestamp required to be within <c>allowedClockSkew</c> of now. An empty secret rejects everything.
/// </summary>
public static class HmacRequestSignature
{
    public const string SignaturePrefix = "sha256=";

    public static bool IsValid(string? sharedSecret, TimeSpan allowedClockSkew, string payload, string? timestamp, string? signature)
    {
        if (string.IsNullOrWhiteSpace(sharedSecret) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!TryParseTimestamp(timestamp, out var parsedTimestamp))
        {
            return false;
        }

        var utcNow = DateTimeOffset.UtcNow;
        if (utcNow - parsedTimestamp > allowedClockSkew || parsedTimestamp - utcNow > allowedClockSkew)
        {
            return false;
        }

        var computedSignature = Compute(sharedSecret, timestamp, payload);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature));
    }

    public static string Compute(string sharedSecret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}\n{payload}"));
        return SignaturePrefix + Convert.ToHexString(computedBytes).ToLowerInvariant();
    }

    public static bool TryParseTimestamp(string? timestamp, out DateTimeOffset parsedTimestamp)
    {
        parsedTimestamp = default;
        return !string.IsNullOrWhiteSpace(timestamp) &&
            DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsedTimestamp);
    }

    public static TimeSpan ReadClockSkew(string? configuredMinutes)
    {
        return TimeSpan.FromMinutes(int.TryParse(configuredMinutes, out var minutes) ? minutes : 5);
    }
}
