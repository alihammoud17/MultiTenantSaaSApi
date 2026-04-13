using Domain.Authorization;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Presentation.Authorization;

namespace Presentation.Controllers;

[Route("api/v1/tenant/analytics")]
[ApiController]
[Authorize(Policy = RbacPolicyNames.AuditLogsRead)]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IUsageAnalyticsService _usageAnalyticsService;
    private readonly IEntitlementEnforcer _entitlementEnforcer;
    private readonly ITenantContext _tenantContext;

    public AnalyticsController(
        IUsageAnalyticsService usageAnalyticsService,
        IEntitlementEnforcer entitlementEnforcer,
        ITenantContext tenantContext)
    {
        _usageAnalyticsService = usageAnalyticsService;
        _entitlementEnforcer = entitlementEnforcer;
        _tenantContext = tenantContext;
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsageSummary(
        [FromQuery] int days = 30,
        [FromQuery] string? action = null,
        CancellationToken cancellationToken = default)
    {
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, _tenantContext.TenantId, EntitlementKeys.AnalyticsAuditLogsRead);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        var summary = await _usageAnalyticsService.GetTenantUsageSummaryAsync(days, action, cancellationToken);
        return Ok(summary);
    }
}
