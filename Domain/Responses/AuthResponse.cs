using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Responses
{
    public record AuthResponse(
        string Token,
        Guid TenantId,
        Guid UserId,
        string Email,
        DateTime ExpiresAt
    );
}
