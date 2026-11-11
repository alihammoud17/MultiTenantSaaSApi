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

    // Known-answer vector published in docs/contracts.md. Producers (BillingService, operator scripts) must
    // reproduce it exactly; if this test needs to change, the published contract changes too.
    [Fact]
    public void Compute_ShouldMatchThePublishedContractTestVector()
    {
        var signature = HmacRequestSignature.Compute(
            "example-shared-secret",
            "2026-01-01T00:00:00Z",
            "{\"eventId\":\"evt_example\"}");

        signature.Should().Be("sha256=b82b6991c877ed60e6a337a1928478d323770ca01846828fa0a77c424b19d8ae");
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
