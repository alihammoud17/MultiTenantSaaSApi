using Domain.Entites;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
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
            var userId = httpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                UserId = Guid.Parse(userId ?? Guid.Empty.ToString()),
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
    }
}
