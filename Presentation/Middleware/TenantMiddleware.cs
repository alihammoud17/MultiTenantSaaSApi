using Domain.Entites;
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
            ApplicationDbContext dbContext)
        {
            // Skip tenant resolution for public endpoints
            var path = context.Request.Path.Value?.ToLower() ?? "";
            if (path.StartsWith("/api/auth") ||
                path.StartsWith("/api/internal/billing") ||
                path.StartsWith("/health") ||
                path.StartsWith("/swagger") ||
                (path == "/api/plans" && HttpMethods.IsGet(context.Request.Method)))
            {
                await _next(context);
                return;
            }

            Guid? tenantId = null;

            // Strategy 1: Extract from subdomain (e.g., acme.yourapi.com)
            var host = context.Request.Host.Host;
            if (host.Contains('.'))
            {
                var subdomain = host.Split('.')[0];
                var tenant = await dbContext.Tenants
                    .FirstOrDefaultAsync(t => t.Subdomain == subdomain);

                if (tenant != null)
                    tenantId = tenant.Id;
            }

            // Strategy 2: Extract from custom header (fallback)
            if (tenantId == null && context.Request.Headers.TryGetValue("X-Tenant-ID", out var headerValue))
            {
                if (Guid.TryParse(headerValue, out var parsedId))
                    tenantId = parsedId;
            }

            // Strategy 3: Extract from JWT claim (if user is authenticated)
            if (tenantId == null && context.User.Identity?.IsAuthenticated == true)
            {
                var claim = context.User.FindFirst("tenant_id");
                if (claim != null && Guid.TryParse(claim.Value, out var parsedId))
                    tenantId = parsedId;
            }

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
            var activeTenant = await dbContext.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId.Value);

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
            _logger.LogInformation("Request tenant identified: {TenantId}", tenantId);

            await _next(context);
        }
    }
}
