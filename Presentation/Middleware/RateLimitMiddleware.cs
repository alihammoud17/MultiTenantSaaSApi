using Domain.Interfaces;

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
            IRateLimitService rateLimitService)
        {
            // Skip for public endpoints
            var path = context.Request.Path.Value?.ToLower() ?? "";
            if (path.StartsWith("/api/auth") ||
                path.StartsWith("/health") ||
                path.StartsWith("/swagger") ||
                path.StartsWith("/api/plans"))
            {
                await _next(context);
                return;
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
                    upgradeUrl = "/api/plans"
                });
                return;
            }

            await _next(context);
        }
    }
}
