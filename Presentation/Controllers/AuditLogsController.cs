using Domain.Authorization;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Presentation.Authorization;

namespace Presentation.Controllers;

[Route("api/tenant/audit-logs")]
[ApiController]
[Authorize(Policy = RbacPolicyNames.AuditLogsRead)]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditService _auditService;
    private readonly IEntitlementEnforcer _entitlementEnforcer;
    private readonly ITenantContext _tenantContext;

    public AuditLogsController(
        IAuditService auditService,
        IEntitlementEnforcer entitlementEnforcer,
        ITenantContext tenantContext)
    {
        _auditService = auditService;
        _entitlementEnforcer = entitlementEnforcer;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetTenantAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null)
    {
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, _tenantContext.TenantId, EntitlementKeys.AnalyticsAuditLogsRead);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        var logs = await _auditService.GetTenantAuditLogsAsync(page, pageSize, action, fromUtc, toUtc);
        return Ok(logs);
    }
}
