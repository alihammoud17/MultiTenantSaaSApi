using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entites
{
    public class Subscription
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string PlanId { get; set; } = string.Empty;
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
        public DateTime CurrentPeriodStart { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Tenant Tenant { get; set; } = null!;
        public Plan Plan { get; set; } = null!;
    }

    public enum SubscriptionStatus
    {
        Active,
        Canceled,
        Expired
    }
}
