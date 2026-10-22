using Domain.Authorization;
using Domain.DTOs;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Presentation.Authorization;

namespace Presentation.Controllers;

[Route("api/v1/admin/tenant/cors-origins")]
[ApiController]
[Authorize]
public class TenantCorsOriginsController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantCorsOriginService _corsOriginService;
    private readonly IEntitlementEnforcer _entitlementEnforcer;
    private readonly IHostEnvironment _environment;

    public TenantCorsOriginsController(
        ITenantContext tenantContext,
        ITenantCorsOriginService corsOriginService,
        IEntitlementEnforcer entitlementEnforcer,
        IHostEnvironment environment)
    {
        _tenantContext = tenantContext;
        _corsOriginService = corsOriginService;
        _entitlementEnforcer = entitlementEnforcer;
        _environment = environment;
    }

    [HttpGet]
    [Authorize(Policy = RbacPolicyNames.TenantsRead)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, _tenantContext.TenantId, EntitlementKeys.SecurityCorsOriginsManage, cancellationToken);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        return Ok(await _corsOriginService.ListAsync(_tenantContext.TenantId, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = RbacPolicyNames.TenantsManage)]
    public async Task<IActionResult> Add([FromBody] AddTenantCorsOriginRequest request, CancellationToken cancellationToken)
    {
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, _tenantContext.TenantId, EntitlementKeys.SecurityCorsOriginsManage, cancellationToken);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        var result = await _corsOriginService.AddAsync(_tenantContext.TenantId, request.Origin, _environment.IsDevelopment(), cancellationToken);
        return result.Outcome switch
        {
            TenantCorsOriginAddOutcome.Added => Created($"/api/v1/admin/tenant/cors-origins/{result.Origin!.Id}", result.Origin),
            TenantCorsOriginAddOutcome.Duplicate => Conflict(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error })
        };
    }

    [HttpDelete("{originId:guid}")]
    [Authorize(Policy = RbacPolicyNames.TenantsManage)]
    public async Task<IActionResult> Remove(Guid originId, CancellationToken cancellationToken)
    {
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, _tenantContext.TenantId, EntitlementKeys.SecurityCorsOriginsManage, cancellationToken);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        return await _corsOriginService.RemoveAsync(_tenantContext.TenantId, originId, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "CORS origin not found" });
    }
}
