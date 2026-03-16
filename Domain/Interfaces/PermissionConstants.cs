namespace Domain.Interfaces
{
    public static class PermissionCodes
    {
        public const string TenantRead = "tenant.read";
        public const string TenantManage = "tenant.manage";
        public const string PlanRead = "plan.read";
        public const string PlanUpgrade = "plan.upgrade";
        public const string AuditRead = "audit.read";
        public const string UserManage = "user.manage";

        public static readonly string[] All =
        [
            TenantRead,
            TenantManage,
            PlanRead,
            PlanUpgrade,
            AuditRead,
            UserManage
        ];
    }

    public static class AuthorizationPolicies
    {
        public const string TenantRead = "TenantRead";
        public const string TenantManage = "TenantManage";
        public const string PlanRead = "PlanRead";
        public const string PlanUpgrade = "PlanUpgrade";
        public const string AuditRead = "AuditRead";
        public const string UserManage = "UserManage";
    }
}
