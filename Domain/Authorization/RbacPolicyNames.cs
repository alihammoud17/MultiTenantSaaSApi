namespace Domain.Authorization
{
    public static class RbacPolicyNames
    {
        private const string Prefix = "Permission:";

        public static string ForPermission(string permission) => $"{Prefix}{permission}";
    }
}
