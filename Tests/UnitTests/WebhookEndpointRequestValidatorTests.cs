using Application.Services;
using FluentAssertions;

namespace Tests.UnitTests;

public class WebhookEndpointRequestValidatorTests
{
    [Theory]
    [InlineData(null, "https://example.com/hook")]
    [InlineData(" ", "https://example.com/hook")]
    [InlineData("primary", null)]
    [InlineData("primary", "")]
    public void MissingNameOrCallbackUrl_ShouldBeRejected(string? name, string? callbackUrl)
    {
        WebhookEndpointRequestValidator.TryValidate(name, callbackUrl, null, out var error).Should().BeFalse();
        error.Should().Be("Name and callbackUrl are required");
    }

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("http://localhost:5000/hook")]
    [InlineData("https://hooks.example.com:8443/path?query=1")]
    public void AbsoluteHttpUrls_ShouldBeAccepted(string callbackUrl)
    {
        WebhookEndpointRequestValidator.TryValidate("primary", callbackUrl, "*", out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("mailto:ops@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    [InlineData("//example.com/hook")]
    public void NonHttpOrHostlessUrls_ShouldBeRejected(string callbackUrl)
    {
        WebhookEndpointRequestValidator.TryValidate("primary", callbackUrl, null, out var error).Should().BeFalse();
        error.Should().Contain("callbackUrl");
    }

    [Fact]
    public void ValuesAtTheirColumnLimits_ShouldBeAccepted()
    {
        var url = "https://example.com/" + new string('p', WebhookEndpointRequestValidator.MaxCallbackUrlLength - "https://example.com/".Length);

        WebhookEndpointRequestValidator.TryValidate(
                new string('n', WebhookEndpointRequestValidator.MaxNameLength),
                url,
                new string('e', WebhookEndpointRequestValidator.MaxSubscribedEventTypesLength),
                out _)
            .Should().BeTrue();
    }
}
