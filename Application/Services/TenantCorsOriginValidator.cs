namespace Application.Services
{
    public static class TenantCorsOriginValidator
    {
        public const int MaxOriginLength = 255;

        /// <summary>
        /// Accepts only an exact origin as a browser sends it: <c>https://host[:port]</c>, with no path (not even a
        /// trailing slash), query, fragment, user info, wildcard or explicit default port. <c>null</c> is rejected.
        /// <c>http://localhost[:port]</c> is accepted only when <paramref name="allowLocalhost"/> is true; localhost
        /// is otherwise rejected under any scheme. The result is lowercased.
        /// </summary>
        public static bool TryNormalize(string? input, bool allowLocalhost, out string origin, out string error)
        {
            origin = string.Empty;
            error = string.Empty;

            var candidate = input?.Trim() ?? string.Empty;
            if (candidate.Length == 0)
            {
                error = "Origin is required.";
                return false;
            }

            if (candidate.Length > MaxOriginLength)
            {
                error = $"Origin must be at most {MaxOriginLength} characters.";
                return false;
            }

            if (candidate.Contains('*'))
            {
                error = "Wildcard origins are not allowed.";
                return false;
            }

            if (string.Equals(candidate, "null", StringComparison.OrdinalIgnoreCase))
            {
                error = "The 'null' origin is not allowed.";
                return false;
            }

            // Browsers send IDN hosts in punycode, so only printable ASCII can ever match an Origin header.
            if (candidate.Any(c => c <= ' ' || c > '~')
                || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                || string.IsNullOrEmpty(uri.Host))
            {
                error = "Origin must be an absolute URL such as https://app.example.com.";
                return false;
            }

            var isLocalhost = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
            if (isLocalhost && !allowLocalhost)
            {
                error = "localhost origins are only allowed in Development.";
                return false;
            }

            var schemeAllowed = uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && isLocalhost);
            if (!schemeAllowed)
            {
                error = allowLocalhost
                    ? "Origin must use https (http is allowed only for localhost)."
                    : "Origin must use https.";
                return false;
            }

            // Anything Uri drops or rewrites (path, '/', query, fragment, user info, default port) makes the input
            // differ from its authority, so requiring equality rejects all of it.
            var normalized = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            {
                error = "Origin must be exactly scheme://host[:port] with no path, trailing slash, query, fragment, credentials or default port.";
                return false;
            }

            origin = normalized;
            return true;
        }
    }
}
