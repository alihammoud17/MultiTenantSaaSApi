namespace Domain.Authorization
{
    public static class RbacPolicyNames
    {
        private const string Prefix = "Permission:";

        public static string TenantsRead => ForPermission(RbacPermissions.TenantsRead);
        public static string TenantsManage => ForPermission(RbacPermissions.TenantsManage);
        public static string UsersRead => ForPermission(RbacPermissions.UsersRead);
        public static string UsersManage => ForPermission(RbacPermissions.UsersManage);
        public static string BillingManage => ForPermission(RbacPermissions.BillingManage);
        public static string AuditLogsRead => ForPermission(RbacPermissions.AuditLogsRead);

        public static string ForPermission(string permission) => $"{Prefix}{permission}";
    }
}
