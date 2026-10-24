using System.Net;
using System.Net.Sockets;
using System.Security.Claims;

namespace Presentation.RateLimiting;

public static class AuthRateLimitPartitionKey
{
    public const string Unknown = "unknown";

    /// <summary>
    /// Builds the per-client partition key for the unauthenticated auth limiters.
    /// IPv4 addresses are keyed individually. IPv6 addresses are grouped by their /64 prefix,
    /// because a single client typically controls a whole /64 and could otherwise rotate addresses freely.
    /// </summary>
    public static string From(IPAddress? address)
    {
        if (address is null)
            return Unknown;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    /// <summary>
    /// Builds the per-user partition key for authenticated auth limiters from the JWT's tenant and subject claims.
    /// Returns null for an unauthenticated caller, or one missing either claim, so the caller can fall back to the
    /// client address.
    /// </summary>
    public static string? FromUser(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        var tenantId = user.FindFirst("tenant_id")?.Value;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
            return null;

        return $"user:{tenantId}:{userId}";
    }
}
