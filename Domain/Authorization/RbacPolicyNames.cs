namespace Domain.Authorization
{
    public static class RbacPolicyNames
    {
        private const string Prefix = "Permission:";

        public const string TenantsRead = Prefix + RbacPermissions.TenantsRead;
        public const string TenantsManage = Prefix + RbacPermissions.TenantsManage;
        public const string UsersRead = Prefix + RbacPermissions.UsersRead;
        public const string UsersManage = Prefix + RbacPermissions.UsersManage;
        public const string BillingManage = Prefix + RbacPermissions.BillingManage;
        public const string AuditLogsRead = Prefix + RbacPermissions.AuditLogsRead;

        public static string ForPermission(string permission) => $"{Prefix}{permission}";
    }
}
