using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Services
{
    public class RateLimitService : IRateLimitService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<RateLimitService> _logger;

        public RateLimitService(
            IConnectionMultiplexer redis,
            ApplicationDbContext dbContext,
            ILogger<RateLimitService> logger)
        {
            _redis = redis;
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<RateLimitResult> CheckRateLimitAsync(Guid tenantId)
        {
            var db = _redis.GetDatabase();

            // Get tenant's subscription and plan
            var subscription = await _dbContext.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (subscription == null)
                throw new InvalidOperationException("Tenant has no subscription");

            var limit = subscription.Plan.ApiCallsPerMonth;
            var now = DateTime.UtcNow;
            var month = now.ToString("yyyy-MM");
            var key = $"ratelimit:{tenantId}:{month}";

            // Get current usage
            var currentUsage = (int)(await db.StringGetAsync(key));

            // Check if limit exceeded
            if (currentUsage >= limit)
            {
                var resetDate = new DateTime(now.Year, now.Month, 1).AddMonths(1);
                return new RateLimitResult(false, limit, 0, resetDate);
            }

            // Increment usage
            await db.StringIncrementAsync(key);

            // Set expiration to end of next month (if not already set)
            var ttl = await db.KeyTimeToLiveAsync(key);
            if (ttl == null)
            {
                var endOfNextMonth = new DateTime(now.Year, now.Month, 1)
                    .AddMonths(2)
                    .AddDays(-1);
                await db.KeyExpireAsync(key, endOfNextMonth);
            }

            var remaining = limit - currentUsage - 1;
            var resetDateFinal = new DateTime(now.Year, now.Month, 1).AddMonths(1);

            return new RateLimitResult(true, limit, remaining, resetDateFinal);
        }
    }
}
