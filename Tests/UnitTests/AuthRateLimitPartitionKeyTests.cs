using System.Net;
using FluentAssertions;
using Presentation.RateLimiting;

namespace Tests.UnitTests;

public class AuthRateLimitPartitionKeyTests
{
    [Fact]
    public void From_ShouldReturnUnknown_WhenAddressIsNull()
    {
        AuthRateLimitPartitionKey.From(null).Should().Be(AuthRateLimitPartitionKey.Unknown);
    }

    [Fact]
    public void From_ShouldKeyIpv4AddressesIndividually()
    {
        AuthRateLimitPartitionKey.From(IPAddress.Parse("198.51.100.1")).Should().Be("198.51.100.1");
        AuthRateLimitPartitionKey.From(IPAddress.Parse("198.51.100.2")).Should().Be("198.51.100.2");
    }

    [Fact]
    public void From_ShouldTreatIpv4MappedIpv6AsIpv4()
    {
        AuthRateLimitPartitionKey.From(IPAddress.Parse("::ffff:198.51.100.1")).Should().Be("198.51.100.1");
    }

    [Fact]
    public void From_ShouldGroupIpv6AddressesBySlash64Prefix()
    {
        var first = AuthRateLimitPartitionKey.From(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
        var sameSlash64 = AuthRateLimitPartitionKey.From(IPAddress.Parse("2001:db8:1:2::1"));
        var otherSlash64 = AuthRateLimitPartitionKey.From(IPAddress.Parse("2001:db8:1:3::1"));

        first.Should().Be("2001:db8:1:2::/64");
        sameSlash64.Should().Be(first);
        otherSlash64.Should().NotBe(first);
    }
}
