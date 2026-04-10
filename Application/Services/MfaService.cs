using Domain.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services
{
    public class MfaService : IMfaService
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public (string Secret, string ProvisioningUri) GenerateEnrollmentSecret(string issuer, string accountName)
        {
            var secret = GenerateBase32Secret();
            var encodedIssuer = Uri.EscapeDataString(issuer);
            var encodedAccount = Uri.EscapeDataString(accountName);
            var provisioningUri = $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={secret}&issuer={encodedIssuer}&digits=6&period=30";
            return (secret, provisioningUri);
        }

        public bool VerifyCode(string secret, string code)
        {
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
                return false;

            if (!int.TryParse(code, out _))
                return false;

            var normalizedCode = code.Trim();
            if (normalizedCode.Length != 6)
                return false;

            var secretBytes = DecodeBase32(secret);
            var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            for (long offset = -1; offset <= 1; offset++)
            {
                if (GenerateTotp(secretBytes, currentStep + offset) == normalizedCode)
                    return true;
            }

            return false;
        }

        public string GenerateOpaqueToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        private static string GenerateBase32Secret()
        {
            Span<byte> secretBytes = stackalloc byte[20];
            RandomNumberGenerator.Fill(secretBytes);
            return EncodeBase32(secretBytes.ToArray());
        }

        private static string GenerateTotp(byte[] secretBytes, long timestepNumber)
        {
            Span<byte> timestep = stackalloc byte[8];
            BitConverter.TryWriteBytes(timestep, timestepNumber);
            if (BitConverter.IsLittleEndian)
                timestep.Reverse();

            using var hmac = new HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(timestep.ToArray());
            var offset = hash[^1] & 0x0F;
            var binaryCode = ((hash[offset] & 0x7f) << 24)
                             | ((hash[offset + 1] & 0xff) << 16)
                             | ((hash[offset + 2] & 0xff) << 8)
                             | (hash[offset + 3] & 0xff);
            var otp = binaryCode % 1_000_000;
            return otp.ToString("D6");
        }

        private static string EncodeBase32(byte[] data)
        {
            var output = new StringBuilder((data.Length + 4) / 5 * 8);
            int bitBuffer = data[0];
            int nextByte = 1;
            int bitsLeft = 8;
            while (bitsLeft > 0 || nextByte < data.Length)
            {
                if (bitsLeft < 5)
                {
                    if (nextByte < data.Length)
                    {
                        bitBuffer <<= 8;
                        bitBuffer |= data[nextByte++] & 0xff;
                        bitsLeft += 8;
                    }
                    else
                    {
                        int pad = 5 - bitsLeft;
                        bitBuffer <<= pad;
                        bitsLeft += pad;
                    }
                }

                int index = 0x1f & (bitBuffer >> (bitsLeft - 5));
                bitsLeft -= 5;
                output.Append(Base32Alphabet[index]);
            }

            return output.ToString();
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
}
