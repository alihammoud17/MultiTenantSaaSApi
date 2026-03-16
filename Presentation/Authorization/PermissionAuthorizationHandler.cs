using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
            var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
            var subjectClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");

            if (!Guid.TryParse(tenantClaim, out var tenantId) || !Guid.TryParse(subjectClaim, out var userId))
            {
                return;
            }

            var hasPermission = await _rbacAuthorizationService.HasPermissionAsync(tenantId, userId, requirement.PermissionCode);
            if (hasPermission)
            {
                context.Succeed(requirement);
            }
        }
    }
}
