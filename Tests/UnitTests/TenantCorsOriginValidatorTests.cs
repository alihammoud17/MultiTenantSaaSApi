using Application.Services;
using FluentAssertions;

namespace Tests.UnitTests;

public class TenantCorsOriginValidatorTests
{
    [Theory]
    [InlineData("https://app.example.com", "https://app.example.com")]
    [InlineData("  HTTPS://App.Example.COM ", "https://app.example.com")]
    [InlineData("https://app.example.com:8443", "https://app.example.com:8443")]
    [InlineData("https://xn--bcher-kva.example", "https://xn--bcher-kva.example")]
    public void ExactHttpsOrigin_ShouldBeAcceptedAndLowercased(string input, string expected)
    {
        TenantCorsOriginValidator.TryNormalize(input, allowLocalhost: false, out var origin, out _).Should().BeTrue();
        origin.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData("app.example.com")]
    [InlineData("//app.example.com")]
    [InlineData("http://app.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com?query=1")]
    [InlineData("https://app.example.com#fragment")]
    [InlineData("https://user:pass@app.example.com")]
    [InlineData("https://app.example.com:443")]
    [InlineData("https://bücher.example")]
    [InlineData("https://app\t.example.com")]
    [InlineData("https://app .example.com")]
    public void NonExactOrNonHttpsOrigin_ShouldBeRejected(string? input)
    {
        TenantCorsOriginValidator.TryNormalize(input, allowLocalhost: true, out var origin, out var error).Should().BeFalse();
        origin.Should().BeEmpty();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void OverlongOrigin_ShouldBeRejected()
    {
        var input = "https://" + new string('a', TenantCorsOriginValidator.MaxOriginLength) + ".example.com";

        TenantCorsOriginValidator.TryNormalize(input, allowLocalhost: false, out _, out var error).Should().BeFalse();
        error.Should().Contain("at most");
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:5001")]
    public void Localhost_ShouldBeAcceptedOnlyWhenAllowed(string input)
    {
        TenantCorsOriginValidator.TryNormalize(input, allowLocalhost: true, out var origin, out _).Should().BeTrue();
        origin.Should().Be(input);

        TenantCorsOriginValidator.TryNormalize(input, allowLocalhost: false, out _, out var error).Should().BeFalse();
        error.Should().Contain("Development");
    }

    [Theory]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("http://localhost.example.com")]
    public void HttpOnAnyHostOtherThanLocalhost_ShouldBeRejected_EvenWhenLocalhostIsAllowed(string input)
    {
        TenantCorsOriginValidator.TryNormalize(input, allowLocalhost: true, out _, out _).Should().BeFalse();
    }
}
