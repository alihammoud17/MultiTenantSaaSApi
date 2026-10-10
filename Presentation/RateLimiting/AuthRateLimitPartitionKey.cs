using System.Net;
using System.Net.Sockets;

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
}
