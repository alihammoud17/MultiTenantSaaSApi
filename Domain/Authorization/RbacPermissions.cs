namespace Domain.Authorization
{
    public static class RbacPermissions
    {
        public const string TenantsRead = "tenants.read";
        public const string TenantsManage = "tenants.manage";
        public const string UsersRead = "users.read";
        public const string UsersManage = "users.manage";
        public const string BillingManage = "billing.manage";
        public const string AuditLogsRead = "auditlogs.read";

        public static readonly string[] All =
        [
            TenantsRead,
            TenantsManage,
            UsersRead,
            UsersManage,
            BillingManage,
            AuditLogsRead
        ];
    }
}
