using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Middleware
{
    public class RateLimitMiddleware
    {
        private readonly RequestDelegate _next;

        public RateLimitMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantContext tenantContext,
            IRateLimitService rateLimitService,
            IRequestTenantAccessContext requestTenantAccessContext,
            ApplicationDbContext dbContext)
        {
            // Skip for public endpoints
            var path = context.Request.Path.Value?.ToLower() ?? "";
            if (ApiVersionedRouteMatcher.IsAuthPath(path) ||
                // Internal billing callbacks are service-to-service signed requests.
                // They are not tenant-authenticated API traffic, so tenant plan limits do not apply here.
                // Tenant/subscription validation is performed by the internal billing callback processor.
                path.StartsWith("/api/internal/billing") ||
                path.StartsWith("/health") ||
                path.StartsWith("/metrics") ||
                path.StartsWith("/swagger") ||
                (ApiVersionedRouteMatcher.IsPlansPath(path) && HttpMethods.IsGet(context.Request.Method)))
            {
                await _next(context);
                return;
            }

            if (requestTenantAccessContext.ApiCallsPerMonthLimit == null)
            {
                var limit = await dbContext.Subscriptions
                    .Where(s => s.TenantId == tenantContext.TenantId)
                    .Select(s => s.Plan.ApiCallsPerMonth)
                    .FirstOrDefaultAsync();

                if (limit > 0)
                {
                    requestTenantAccessContext.SetApiCallsPerMonthLimit(limit);
                }
            }

            var result = await rateLimitService.CheckRateLimitAsync(tenantContext.TenantId);

            // Add rate limit headers
            context.Response.Headers.Append("X-RateLimit-Limit", result.Limit.ToString());
            context.Response.Headers.Append("X-RateLimit-Remaining", result.Remaining.ToString());
            context.Response.Headers.Append("X-RateLimit-Reset", result.ResetDate.ToString("o"));

            if (!result.IsAllowed)
            {
                context.Response.StatusCode = 429;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "RateLimitExceeded",
                    message = $"You've exceeded your plan limit of {result.Limit} API calls per month",
                    limit = result.Limit,
                    resetDate = result.ResetDate,
                    upgradeUrl = "/api/v1/plans"
                });
                return;
            }

            await _next(context);
        }
    }
}
