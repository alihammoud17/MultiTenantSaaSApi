using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Middleware
{
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantMiddleware> _logger;

        public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantContext tenantContext,
            IRequestTenantResolutionCache tenantResolutionCache,
            ApplicationDbContext dbContext)
        {
            // Skip tenant resolution for public endpoints
            var path = context.Request.Path.Value?.ToLower() ?? "";
            if (ApiVersionedRouteMatcher.IsAuthPath(path) ||
                path.StartsWith("/api/internal/billing") ||
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

            // Strategy 1: Extract from subdomain (e.g., acme.yourapi.com)
            var host = context.Request.Host.Host;
            if (host.Contains('.'))
            {
                var subdomain = host.Split('.')[0];
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
            Guid? jwtTenantId = null;
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var claim = context.User.FindFirst("tenant_id");
                if (claim != null && Guid.TryParse(claim.Value, out var parsedId))
                    jwtTenantId = parsedId;
            }

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

            if (activeTenant == null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "TenantNotFound",
                    message = "Tenant does not exist"
                });
                return;
            }

            if (activeTenant.Status != TenantStatus.Active)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "TenantSuspended",
                    message = "This tenant account is suspended"
                });
                return;
            }

            // Set tenant context for this request
            tenantContext.SetTenantId(tenantId.Value);
            tenantResolutionCache.SetResolvedTenant(activeTenant);
            _logger.LogInformation("Request tenant identified: {TenantId}", tenantId);

            await _next(context);
        }
    }
}
