using FluentAssertions;
using Presentation.Middleware;

namespace Tests.UnitTests;

public class TenantResolutionOptionsTests
{
    [Theory]
    [InlineData("acme.example.com", "example.com", "acme")]
    [InlineData("ACME.Example.COM", "example.com", "acme")]
    [InlineData("acme.example.com.", "example.com", "acme")]
    [InlineData("acme.example.com", ".Example.com", "acme")]
    public void TryGetTenantSubdomain_ShouldReturnLabel_ForSingleLabelUnderBaseDomain(string host, string baseDomain, string expected)
    {
        TenantResolutionOptions.TryGetTenantSubdomain(host, baseDomain, out var subdomain).Should().BeTrue();
        subdomain.Should().Be(expected);
    }

    [Theory]
    [InlineData("acme.example.com", null)]
    [InlineData("acme.example.com", "")]
    [InlineData("acme.example.com", "   ")]
    [InlineData("acme.other.com", "example.com")]
    [InlineData("acme.notexample.com", "example.com")]
    [InlineData("example.com", "example.com")]
    [InlineData("a.b.example.com", "example.com")]
    [InlineData("127.0.0.1", "example.com")]
    [InlineData("localhost", "example.com")]
    [InlineData("", "example.com")]
    [InlineData(null, "example.com")]
    public void TryGetTenantSubdomain_ShouldReturnFalse_ForNonTenantHosts(string? host, string? baseDomain)
    {
        TenantResolutionOptions.TryGetTenantSubdomain(host, baseDomain, out var subdomain).Should().BeFalse();
        subdomain.Should().BeEmpty();
    }

    [Theory]
    [InlineData("api.example.com")]
    [InlineData("WWW.example.com")]
    [InlineData("admin.example.com")]
    public void TryGetTenantSubdomain_ShouldReturnFalse_ForReservedLabels(string host)
    {
        TenantResolutionOptions.TryGetTenantSubdomain(host, "example.com", out _).Should().BeFalse();
    }
}
