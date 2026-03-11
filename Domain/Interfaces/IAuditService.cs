using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Interfaces
{
    public interface IAuditService
    {
        Task LogAsync(string action, string entityType, string entityId, object? changes = null);
    }
}
