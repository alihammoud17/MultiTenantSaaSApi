using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Domain.DTOs;

namespace Application.Services
{
    public sealed record TenantRegistrationValidationResult(
        RegisterTenantRequest? NormalizedRequest,
        string? Error)
    {
        public bool IsValid => NormalizedRequest is not null;
    }

    /// <summary>
    /// Validates and normalizes anonymous tenant registration input before it reaches the database.
    /// Length limits mirror the EF Core column configuration in ApplicationDbContext.
    /// </summary>
    public static partial class TenantRegistrationValidator
    {
        public const int CompanyNameMaxLength = 100;
        public const int SubdomainMinLength = 3;
        public const int SubdomainMaxLength = 50;
        public const int EmailMaxLength = 255;
        public const int PasswordMinLength = 8;

        // BCrypt only uses the first 72 bytes of a password; longer input would be silently truncated.
        public const int PasswordMaxBytes = 72;

        // Labels that collide with platform hosts or common infrastructure names. TenantMiddleware resolves
        // tenants from the first host label, so a tenant owning one of these could hijack host-based resolution.
        private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.Ordinal)
        {
            "admin", "api", "app", "assets", "auth", "billing", "cdn", "dashboard", "docs", "ftp",
            "internal", "localhost", "mail", "smtp", "static", "status", "support", "www"
        };

        // Lowercase DNS label: letters, digits and hyphens, no leading or trailing hyphen.
        [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
        private static partial Regex SubdomainPattern();

        public static TenantRegistrationValidationResult Validate(RegisterTenantRequest request)
        {
            var companyName = request.CompanyName?.Trim() ?? string.Empty;
            var subdomain = request.Subdomain?.Trim().ToLowerInvariant() ?? string.Empty;
            var adminEmail = request.AdminEmail?.Trim() ?? string.Empty;
            var adminPassword = request.AdminPassword ?? string.Empty;

            if (companyName.Length == 0)
                return Invalid("Company name is required");
            if (companyName.Length > CompanyNameMaxLength)
                return Invalid($"Company name must be at most {CompanyNameMaxLength} characters");

            if (subdomain.Length < SubdomainMinLength || subdomain.Length > SubdomainMaxLength)
                return Invalid($"Subdomain must be between {SubdomainMinLength} and {SubdomainMaxLength} characters");
            if (!SubdomainPattern().IsMatch(subdomain))
                return Invalid("Subdomain may only contain lowercase letters, digits and hyphens, and cannot start or end with a hyphen");
            if (subdomain.All(char.IsAsciiDigit))
                return Invalid("Subdomain cannot be entirely numeric");
            if (ReservedSubdomains.Contains(subdomain))
                return Invalid("Subdomain is reserved");

            if (adminEmail.Length == 0 || adminEmail.Length > EmailMaxLength || !IsValidEmail(adminEmail))
                return Invalid("Admin email is invalid");

            if (adminPassword.Length < PasswordMinLength)
                return Invalid($"Admin password must be at least {PasswordMinLength} characters");
            if (Encoding.UTF8.GetByteCount(adminPassword) > PasswordMaxBytes)
                return Invalid($"Admin password must be at most {PasswordMaxBytes} bytes");

            return new TenantRegistrationValidationResult(
                new RegisterTenantRequest(companyName, subdomain, adminEmail, adminPassword),
                null);
        }

        private static bool IsValidEmail(string email)
        {
            // Reject display-name forms such as "Name <a@b.com>" by requiring the parsed address to match the input.
            return MailAddress.TryCreate(email, out var address)
                && string.Equals(address.Address, email, StringComparison.Ordinal);
        }

        private static TenantRegistrationValidationResult Invalid(string error) => new(null, error);
    }
}
