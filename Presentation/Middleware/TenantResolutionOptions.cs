using Application.Services;

namespace Presentation.Middleware
{
    public sealed class TenantResolutionOptions
    {
        public const string SectionName = "TenantResolution";

        /// <summary>
        /// Domain under which tenants are served as <c>&lt;subdomain&gt;.&lt;BaseDomain&gt;</c>, e.g. <c>example.com</c>.
        /// When unset, host-based tenant resolution is disabled and only <c>X-Tenant-ID</c> and the JWT claim are used.
        /// </summary>
        public string? BaseDomain { get; set; }

        /// <summary>
        /// Returns the tenant subdomain label when <paramref name="host"/> is exactly one label under
        /// <paramref name="baseDomain"/>. Any other host (a different domain, the base domain itself, deeper
        /// subdomains, IP addresses, localhost) is not a tenant host. Reserved labels are never treated as tenants,
        /// so platform hosts such as <c>api.&lt;BaseDomain&gt;</c> cannot be captured by tenant data.
        /// </summary>
        public static bool TryGetTenantSubdomain(string? host, string? baseDomain, out string subdomain)
        {
            subdomain = string.Empty;

            var normalizedBase = baseDomain?.Trim().Trim('.').ToLowerInvariant();
            var normalizedHost = host?.Trim().TrimEnd('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedBase) || string.IsNullOrEmpty(normalizedHost))
                return false;

            var suffix = "." + normalizedBase;
            if (!normalizedHost.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            var label = normalizedHost[..^suffix.Length];
            if (label.Length == 0 || label.Contains('.') || TenantRegistrationValidator.IsReservedSubdomain(label))
                return false;

            subdomain = label;
            return true;
        }
    }
}
