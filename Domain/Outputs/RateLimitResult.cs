using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Outputs
{
    public record RateLimitResult(
        bool IsAllowed,
        int Limit,
        int Remaining,
        DateTime ResetDate
    );
}
