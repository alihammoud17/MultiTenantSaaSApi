namespace Domain.Authorization;

public static class EntitlementKeys
{
    public const string BillingInvoicesRead = "feature.billing.invoices.read";
    public const string BillingSubscriptionManage = "feature.billing.subscription.manage";
    public const string BillingPlanUpgrade = "feature.billing.plan.upgrade";
    public const string AnalyticsAuditLogsRead = "feature.analytics.audit_logs.read";
    public const string AdminAdvancedUserManagement = "feature.admin.advanced.user_management";
    public const string ModulesFutureHook = "feature.modules.future.hooks";
}
