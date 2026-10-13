using Application.Services;
using FluentAssertions;

namespace Tests.UnitTests;

public class HmacRequestSignatureTests
{
    private const string Secret = "unit-test-secret";
    private const string Payload = "{\"reason\":\"abuse\"}";
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    [Fact]
    public void IsValid_ShouldAcceptSignatureComputedWithTheSameSecret()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signature = HmacRequestSignature.Compute(Secret, timestamp, Payload);

        signature.Should().StartWith(HmacRequestSignature.SignaturePrefix);
        HmacRequestSignature.IsValid(Secret, Skew, Payload, timestamp, signature).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ShouldRejectSignatureFromADifferentSecret()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signature = HmacRequestSignature.Compute("other-secret", timestamp, Payload);

        HmacRequestSignature.IsValid(Secret, Skew, Payload, timestamp, signature).Should().BeFalse();
    }

    [Fact]
    public void IsValid_ShouldRejectTamperedPayload()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signature = HmacRequestSignature.Compute(Secret, timestamp, Payload);

        HmacRequestSignature.IsValid(Secret, Skew, "{\"reason\":\"other\"}", timestamp, signature).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_ShouldRejectEverything_WhenSecretIsEmpty(string? secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signature = HmacRequestSignature.Compute("", timestamp, Payload);

        HmacRequestSignature.IsValid(secret, Skew, Payload, timestamp, signature).Should().BeFalse();
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(6)]
    public void IsValid_ShouldRejectTimestampOutsideTheSkewWindow(int minutesFromNow)
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(minutesFromNow).ToString("O");
        var signature = HmacRequestSignature.Compute(Secret, timestamp, Payload);

        HmacRequestSignature.IsValid(Secret, Skew, Payload, timestamp, signature).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "sha256=abc")]
    [InlineData("", "sha256=abc")]
    [InlineData("not-a-timestamp", "sha256=abc")]
    [InlineData("2026-10-13T12:00:00Z", null)]
    [InlineData("2026-10-13T12:00:00Z", "")]
    public void IsValid_ShouldRejectMissingOrMalformedHeaders(string? timestamp, string? signature)
    {
        HmacRequestSignature.IsValid(Secret, Skew, Payload, timestamp, signature).Should().BeFalse();
    }
}
