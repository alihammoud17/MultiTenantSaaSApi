using Domain.Outputs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Interfaces
{
    public interface IRateLimitService
    {
        Task<RateLimitResult> CheckRateLimitAsync(Guid tenantId);
    }
}
