using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;


namespace Presentation.Authorization
{
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IRbacAuthorizationService _rbacAuthorizationService;

        public PermissionAuthorizationHandler(IRbacAuthorizationService rbacAuthorizationService)
        {
            _rbacAuthorizationService = rbacAuthorizationService;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var userIdClaim = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var tenantIdClaim = context.User.FindFirstValue("tenant_id");

            if (!Guid.TryParse(userIdClaim, out var userId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
                return;

            var hasPermission = await _rbacAuthorizationService.HasPermissionAsync(tenantId, userId, requirement.Permission);

            if (hasPermission)
                context.Succeed(requirement);
        }
    }
}
