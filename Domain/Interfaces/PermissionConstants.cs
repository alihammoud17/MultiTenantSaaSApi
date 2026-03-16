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

        public static readonly IReadOnlyDictionary<string, Guid> SeedIds = new Dictionary<string, Guid>
        {
            [TenantRead] = new("906f554a-a536-573a-b8e4-3800fc5a9c2d"),
            [TenantManage] = new("c42303e5-d438-5838-a064-f7b5ff09731b"),
            [PlanRead] = new("80cfac1b-d3ca-543c-97c5-a30fcddaa4d9"),
            [PlanUpgrade] = new("a94b8414-288f-544b-84d9-e23ed02f3bae"),
            [AuditRead] = new("1700e29c-2326-5f19-893d-843a0e00ba30"),
            [UserManage] = new("0e06702d-f693-5226-8367-4e1f29a4dce8")
        };
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
