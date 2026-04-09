using Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Authorization;

public static class EntitlementGateExtensions
{
    public static async Task<IActionResult?> EnforceFeatureAsync(
        this ControllerBase controller,
        IEntitlementEnforcer entitlementEnforcer,
        Guid tenantId,
        string entitlementKey,
        CancellationToken cancellationToken = default)
    {
        var enforcement = await entitlementEnforcer.EnsureFeatureEnabledAsync(tenantId, entitlementKey, cancellationToken);
        if (enforcement.Allowed)
        {
            return null;
        }

        return controller.StatusCode(StatusCodes.Status403Forbidden, new { error = enforcement.DenialReason });
    }
}
