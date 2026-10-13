using Domain.Outputs;
﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Interfaces
{
    public interface IAuditService
    {
        // actorUserId records who acted when the request itself is anonymous (register, login, refresh, invite accept);
        // otherwise the authenticated user's claims are used.
        Task LogAsync(string action, string entityType, string entityId, object? changes = null, Guid? actorUserId = null);
        Task<IReadOnlyCollection<TenantAuditLogItem>> GetTenantAuditLogsAsync(
            int page = 1,
            int pageSize = 50,
            string? action = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null);
    }
}
