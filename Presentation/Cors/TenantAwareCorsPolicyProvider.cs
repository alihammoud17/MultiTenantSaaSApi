using Domain.Interfaces;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Presentation.Middleware;

namespace Presentation.Cors
{
    /// <summary>
    /// Evaluates <see cref="ApiCorsPolicy.PolicyName"/>: origins from <c>Cors:AllowedOrigins</c> get the configured
    /// policy (with credentials). Any other origin is allowed, without credentials, when an active tenant registered
    /// it: the tenant served at the request host when the host is a tenant subdomain, otherwise any active tenant,
    /// because preflight requests carry no credentials to identify one. Development keeps allowing any origin.
    /// </summary>
    public sealed class TenantAwareCorsPolicyProvider : ICorsPolicyProvider
    {
        private readonly DefaultCorsPolicyProvider _configuredPolicies;
        private readonly ITenantCorsOriginService _tenantOrigins;
        private readonly IHostEnvironment _environment;
        private readonly IOptionsMonitor<TenantResolutionOptions> _resolutionOptions;

        public TenantAwareCorsPolicyProvider(
            IOptions<CorsOptions> corsOptions,
            ITenantCorsOriginService tenantOrigins,
            IHostEnvironment environment,
            IOptionsMonitor<TenantResolutionOptions> resolutionOptions)
        {
            _configuredPolicies = new DefaultCorsPolicyProvider(corsOptions);
            _tenantOrigins = tenantOrigins;
            _environment = environment;
            _resolutionOptions = resolutionOptions;
        }

        public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
        {
            var configuredPolicy = await _configuredPolicies.GetPolicyAsync(context, policyName);
            if (configuredPolicy is null || policyName != ApiCorsPolicy.PolicyName || _environment.IsDevelopment())
                return configuredPolicy;

            var origin = context.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin) || configuredPolicy.IsOriginAllowed(origin))
                return configuredPolicy;

            var tenantSubdomain = TenantResolutionOptions.TryGetTenantSubdomain(
                context.Request.Host.Host,
                _resolutionOptions.CurrentValue.BaseDomain,
                out var subdomain)
                ? subdomain
                : null;

            if (!await _tenantOrigins.IsOriginAllowedAsync(origin, tenantSubdomain, context.RequestAborted))
                return configuredPolicy; // does not allow the origin, so no CORS headers are sent

            // Credentials are not allowed: the origin is tenant-controlled and may be matched across tenants.
            // A predicate (instead of WithOrigins) makes the middleware send Vary: Origin.
            return new CorsPolicyBuilder()
                .SetIsOriginAllowed(candidate => string.Equals(candidate, origin, StringComparison.Ordinal))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .Build();
        }
    }
}
