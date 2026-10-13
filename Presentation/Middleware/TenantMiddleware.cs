using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Presentation.Middleware
{
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantMiddleware> _logger;
        private readonly IOptionsMonitor<TenantResolutionOptions> _resolutionOptions;

        public TenantMiddleware(
            RequestDelegate next,
            ILogger<TenantMiddleware> logger,
            IOptionsMonitor<TenantResolutionOptions> resolutionOptions)
        {
            _next = next;
            _logger = logger;
            _resolutionOptions = resolutionOptions;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantContext tenantContext,
            IRequestTenantResolutionCache tenantResolutionCache,
            ApplicationDbContext dbContext)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // Auth routes run before a tenant context exists (register, login, refresh), so they skip resolution.
            // An authenticated caller still belongs to a tenant, and that tenant must be active: otherwise a token
            // issued before a suspension would keep working on invites, MFA, step-up and session routes.
            if (ApiVersionedRouteMatcher.IsAuthPath(path))
            {
                var authenticatedTenantId = GetJwtTenantId(context);
                if (authenticatedTenantId != null)
                {
                    var authenticatedTenant = await dbContext.Tenants
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == authenticatedTenantId.Value);

                    if (!await EnsureTenantIsActiveAsync(context, authenticatedTenant))
                        return;
                }

                await _next(context);
                return;
            }

            // Skip tenant resolution for public endpoints
            if (path.StartsWith("/api/internal/billing") ||
                path.StartsWith("/api/internal/tenants") ||
                path.StartsWith("/health") ||
                path.StartsWith("/metrics") ||
                path.StartsWith("/swagger") ||
                (ApiVersionedRouteMatcher.IsPlansPath(path) && HttpMethods.IsGet(context.Request.Method)))
            {
                await _next(context);
                return;
            }

            Guid? requestTenantHint = null;
            Tenant? resolvedTenant = null;

            // Strategy 1: Extract from subdomain, only for hosts of the form <subdomain>.<TenantResolution:BaseDomain>
            if (TenantResolutionOptions.TryGetTenantSubdomain(
                    context.Request.Host.Host,
                    _resolutionOptions.CurrentValue.BaseDomain,
                    out var subdomain))
            {
                resolvedTenant = await dbContext.Tenants
                    .FirstOrDefaultAsync(t => t.Subdomain == subdomain);

                if (resolvedTenant != null)
                    requestTenantHint = resolvedTenant.Id;
            }

            // Strategy 2: Extract from custom header (fallback)
            if (requestTenantHint == null && context.Request.Headers.TryGetValue("X-Tenant-ID", out var headerValue))
            {
                if (Guid.TryParse(headerValue, out var parsedId))
                    requestTenantHint = parsedId;
            }

            // Strategy 3: Resolve from JWT claim when user is authenticated (authoritative for authenticated requests)
            var jwtTenantId = GetJwtTenantId(context);

            if (jwtTenantId != null && requestTenantHint != null && jwtTenantId != requestTenantHint)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "TenantMismatch",
                    message = "Authenticated tenant does not match request tenant context"
                });
                return;
            }

            var tenantId = jwtTenantId ?? requestTenantHint;

            if (tenantId == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "TenantNotFound",
                    message = "Unable to identify tenant from request"
                });
                return;
            }

            // Verify tenant is active
            var activeTenant = resolvedTenant?.Id == tenantId.Value
                ? resolvedTenant
                : await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId.Value);

            if (!await EnsureTenantIsActiveAsync(context, activeTenant))
                return;

            // Set tenant context for this request
            tenantContext.SetTenantId(tenantId.Value);
            tenantResolutionCache.SetResolvedTenant(activeTenant!); // non-null: checked by EnsureTenantIsActiveAsync
            _logger.LogInformation("Request tenant identified: {TenantId}", tenantId);

            await _next(context);
        }

        private static Guid? GetJwtTenantId(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return null;

            var claim = context.User.FindFirst("tenant_id");
            return claim != null && Guid.TryParse(claim.Value, out var parsedId) ? parsedId : null;
        }

        // Writes the 404/403 response and returns false unless the tenant exists and is active.
        private static async Task<bool> EnsureTenantIsActiveAsync(HttpContext context, Tenant? tenant)
        {
            if (tenant == null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "TenantNotFound",
                    message = "Tenant does not exist"
                });
                return false;
            }

            if (tenant.Status != TenantStatus.Active)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "TenantSuspended",
                    message = "This tenant account is suspended"
                });
                return false;
            }

            return true;
        }
    }
}
