using System.Security.Cryptography;
using System.Text;
using Application.Services;
using FluentAssertions;

namespace Tests.UnitTests;

public class MfaServiceTests
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    [Fact]
    public void GenerateEnrollmentSecret_ShouldReturnBase32Secret_AndEncodedProvisioningUri()
    {
        var sut = new MfaService();

        var result = sut.GenerateEnrollmentSecret("Acme & Co", "user+test@example.com");

        result.Secret.Should().NotBeNullOrWhiteSpace();
        result.Secret.Should().MatchRegex("^[A-Z2-7]+$");
        result.Secret.Length.Should().Be(32);

        result.ProvisioningUri.Should().Be(
            $"otpauth://totp/Acme%20%26%20Co:user%2Btest%40example.com?secret={result.Secret}&issuer=Acme%20%26%20Co&digits=6&period=30");
    }

    [Fact]
    public void VerifyCode_ShouldReturnTrue_ForCurrentWindowAndAdjacentDriftWindows()
    {
        var sut = new MfaService();
        const string secret = "JBSWY3DPEHPK3PXP";
        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        var previousCode = GenerateTotp(secret, currentStep - 1);
        var currentCode = GenerateTotp(secret, currentStep);
        var nextCode = GenerateTotp(secret, currentStep + 1);

        sut.VerifyCode(secret, previousCode).Should().BeTrue();
        sut.VerifyCode(secret, currentCode).Should().BeTrue();
        sut.VerifyCode(secret, nextCode).Should().BeTrue();
    }

    [Fact]
    public void VerifyCode_ShouldReturnFalse_ForNullWhitespaceAndMalformedInput()
    {
        var sut = new MfaService();

        sut.VerifyCode(" ", "123456").Should().BeFalse();
        sut.VerifyCode("JBSWY3DPEHPK3PXP", " ").Should().BeFalse();
        sut.VerifyCode("JBSWY3DPEHPK3PXP", "12A456").Should().BeFalse();
        sut.VerifyCode("JBSWY3DPEHPK3PXP", "12345").Should().BeFalse();
        sut.VerifyCode("JBSWY3DPEHPK3PXP", "1234567").Should().BeFalse();
    }

    [Fact]
    public void VerifyCode_ShouldThrowFormatException_ForInvalidBase32Secret()
    {
        var sut = new MfaService();

        var act = () => sut.VerifyCode("INVALID*SECRET", "123456");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void GenerateOpaqueToken_ShouldReturnUrlSafeUnpaddedHighEntropyToken()
    {
        var sut = new MfaService();

        var tokenA = sut.GenerateOpaqueToken();
        var tokenB = sut.GenerateOpaqueToken();

        tokenA.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        tokenB.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        tokenA.Should().NotBe(tokenB);
    }

    [Fact]
    public void HashToken_ShouldReturnDeterministicUpperHexSha256()
    {
        var sut = new MfaService();
        const string token = "opaque-token-value";

        var hash = sut.HashToken(token);

        hash.Should().Be("0FDDE61B3874C8AE97AB0B89A3821F7A81B9423F7686DE6C98FCEA9B3C16982D");
        hash.Should().MatchRegex("^[0-9A-F]{64}$");
        sut.HashToken(token).Should().Be(hash);
    }

    private static string GenerateTotp(string base32Secret, long timestep)
    {
        var secretBytes = DecodeBase32(base32Secret);

        Span<byte> timestepBytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(timestepBytes, timestep);
        if (BitConverter.IsLittleEndian)
            timestepBytes.Reverse();

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(timestepBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
                         | ((hash[offset + 1] & 0xff) << 16)
                         | ((hash[offset + 2] & 0xff) << 8)
                         | (hash[offset + 3] & 0xff);
        var otp = binaryCode % 1_000_000;
        return otp.ToString("D6");
    }

    private static byte[] DecodeBase32(string input)
    {
        var sanitized = input.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(sanitized.Length * 5 / 8);
        int bitBuffer = 0;
        int bitsInBuffer = 0;

        foreach (var character in sanitized)
        {
            var value = Base32Alphabet.IndexOf(character);
            if (value < 0)
                throw new FormatException("Invalid base32 secret.");

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }

        return bytes.ToArray();
    }
}
