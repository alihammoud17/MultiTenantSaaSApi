using System;

namespace Domain.Entites
{
    public class Subscription
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string PlanId { get; set; } = string.Empty;
        public string? ScheduledPlanId { get; set; }
        public DateTime? ScheduledPlanEffectiveAtUtc { get; set; }
        public DateTime? GracePeriodEndsAtUtc { get; set; }
        public DateTime? CanceledAtUtc { get; set; }
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
        GracePeriod,
        Canceled,
        Expired
    }
}
