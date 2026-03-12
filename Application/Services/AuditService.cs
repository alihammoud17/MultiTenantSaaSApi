using Domain.Entites;
using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Application.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(
            ApplicationDbContext dbContext,
            ITenantContext tenantContext,
            IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(string action, string entityType, string entityId, object? changes = null)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var userId = httpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext?.User.FindFirst(ClaimTypes.Name)?.Value
                ?? httpContext?.User.FindFirst("sub")?.Value;

            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                UserId = Guid.TryParse(userId, out var parsedUserId) ? parsedUserId : Guid.Empty,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Changes = changes != null ? JsonSerializer.Serialize(changes) : null,
                Timestamp = DateTime.UtcNow,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            };

            await _dbContext.AuditLogs.AddAsync(auditLog);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<IReadOnlyCollection<TenantAuditLogItem>> GetTenantAuditLogsAsync(
            int page = 1,
            int pageSize = 50,
            string? action = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _dbContext.AuditLogs
                .AsNoTracking()
                .Where(x => x.TenantId == _tenantContext.TenantId);

            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(x => x.Action == action);
            }

            if (fromUtc.HasValue)
            {
                query = query.Where(x => x.Timestamp >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(x => x.Timestamp <= toUtc.Value);
            }

            var logs = await query
                .OrderByDescending(x => x.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new TenantAuditLogItem(
                    x.Id,
                    x.TenantId,
                    x.UserId,
                    x.Action,
                    x.EntityType,
                    x.EntityId,
                    x.Changes,
                    x.Timestamp,
                    x.IpAddress))
                .ToListAsync();

            return logs;
        }
    }
}
