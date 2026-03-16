using Domain.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Presentation.Authorization
{
    public static class RbacAuthorizationRegistrationExtensions
    {
        public static IServiceCollection AddRbacAuthorization(this IServiceCollection services)
        {
            services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

            services.AddAuthorization(options =>
            {
                foreach (var permission in RbacPermissions.All)
                {
                    options.AddPolicy(RbacPolicyNames.ForPermission(permission), policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.Requirements.Add(new PermissionRequirement(permission));
                    });
                }
            });

            return services;
        }
    }
}
