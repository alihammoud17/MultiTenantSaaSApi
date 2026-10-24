using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Net.Sockets;

namespace Application.Services
{
    public class RateLimitService : IRateLimitService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ApplicationDbContext _dbContext;
        private readonly IRequestTenantAccessContext _requestTenantAccessContext;
        private readonly ILogger<RateLimitService> _logger;

        public RateLimitService(
            IConnectionMultiplexer redis,
            ApplicationDbContext dbContext,
            IRequestTenantAccessContext requestTenantAccessContext,
            ILogger<RateLimitService> logger)
        {
            _redis = redis;
            _dbContext = dbContext;
            _requestTenantAccessContext = requestTenantAccessContext;
            _logger = logger;
        }

        public async Task<RateLimitResult> CheckRateLimitAsync(Guid tenantId)
        {
            var limit = _requestTenantAccessContext.ApiCallsPerMonthLimit;
            if (limit == null)
            {
                // Fallback query path for requests where no per-request rate-limit context was preloaded.
                var subscription = await _dbContext.Subscriptions
                    .Include(s => s.Plan)
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId);

                if (subscription == null)
                    throw new InvalidOperationException("Tenant has no subscription");

                limit = subscription.Plan.ApiCallsPerMonth;
                _requestTenantAccessContext.SetApiCallsPerMonthLimit(limit.Value);
            }

            var now = DateTime.UtcNow;

            var currentMonthStartUtc = new DateTime(
                now.Year,
                now.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc);

            var resetDate = currentMonthStartUtc.AddMonths(1);

            try
            {
                var db = _redis.GetDatabase();
                var month = now.ToString("yyyy-MM");
                var key = $"ratelimit:{tenantId}:{month}";

                // Increment first and check the returned count: INCR is atomic, so concurrent requests each get a
                // distinct count and no more than the limit can pass. Reading the count before incrementing let every
                // request in a burst see the same value and all pass.
                var usage = await db.StringIncrementAsync(key);

                if (usage > limit.Value)
                {
                    // Undo the rejected request's increment so the counter keeps counting accepted calls only, e.g. so
                    // a mid-month plan upgrade is not eaten up by calls that were turned away.
                    await db.StringDecrementAsync(key);
                    return new RateLimitResult(false, limit.Value, 0, resetDate);
                }

                // Set expiration to end of next month (if not already set)
                var ttl = await db.KeyTimeToLiveAsync(key);
                if (ttl == null)
                {
                    var expirationUtc = currentMonthStartUtc.AddMonths(2);

                    await db.KeyExpireAsync(key, expirationUtc);
                }

                var remaining = limit.Value - (int)usage;
                return new RateLimitResult(true, limit.Value, remaining, resetDate);
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogWarning(ex, "Redis unavailable. Skipping rate limit checks for tenant {TenantId}", tenantId);
            }
            catch (RedisTimeoutException ex)
            {
                _logger.LogWarning(ex, "Redis timeout. Skipping rate limit checks for tenant {TenantId}", tenantId);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Redis socket error. Skipping rate limit checks for tenant {TenantId}", tenantId);
            }

            return new RateLimitResult(true, limit.Value, limit.Value, resetDate);
        }
    }
}
