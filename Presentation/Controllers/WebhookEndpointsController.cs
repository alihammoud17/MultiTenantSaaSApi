using Domain.Authorization;
using Domain.DTOs;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Controllers;

[Route("api/v1/tenant/webhook-endpoints")]
[ApiController]
[Authorize]
public class WebhookEndpointsController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly IWebhookEndpointManagementService _managementService;

    public WebhookEndpointsController(ITenantContext tenantContext, IWebhookEndpointManagementService managementService)
    {
        _tenantContext = tenantContext;
        _managementService = managementService;
    }

    [HttpPost]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> Create([FromBody] CreateWebhookEndpointRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.CallbackUrl))
        {
            return BadRequest(new { error = "Name and callbackUrl are required" });
        }

        var created = await _managementService.CreateEndpointAsync(_tenantContext.TenantId, request.Name.Trim(), request.CallbackUrl.Trim(), request.SubscribedEventTypes ?? "*", cancellationToken);
        return Created($"/api/v1/tenant/webhook-endpoints/{created.Endpoint.Id}", new
        {
            endpoint = created.Endpoint,
            secret = new WebhookEndpointSecretResponse(created.SigningSecret, created.HasPendingSecretRotation)
        });
    }

    [HttpGet]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _managementService.ListEndpointsAsync(_tenantContext.TenantId, cancellationToken));

    [HttpPut("{endpointId:guid}")]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> Update(Guid endpointId, [FromBody] UpdateWebhookEndpointRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.CallbackUrl))
        {
            return BadRequest(new { error = "Name and callbackUrl are required" });
        }

        var updated = await _managementService.UpdateEndpointAsync(_tenantContext.TenantId, endpointId, request.Name.Trim(), request.CallbackUrl.Trim(), request.SubscribedEventTypes ?? "*", cancellationToken);
        return updated is null ? NotFound(new { error = "Webhook endpoint not found" }) : Ok(updated);
    }

    [HttpPatch("{endpointId:guid}/status")]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> SetStatus(Guid endpointId, [FromBody] SetWebhookEndpointStatusRequest request, CancellationToken cancellationToken)
    {
        var updated = await _managementService.SetEndpointActiveStateAsync(_tenantContext.TenantId, endpointId, request.IsActive, cancellationToken);
        return updated is null ? NotFound(new { error = "Webhook endpoint not found" }) : Ok(updated);
    }

    [HttpPost("{endpointId:guid}/rotate-secret")]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> RotateSecret(Guid endpointId, CancellationToken cancellationToken)
    {
        var rotated = await _managementService.RotateSigningSecretAsync(_tenantContext.TenantId, endpointId, cancellationToken);
        if (rotated is null)
        {
            return NotFound(new { error = "Webhook endpoint not found" });
        }

        return Ok(new
        {
            endpoint = rotated.Endpoint,
            secret = new WebhookEndpointSecretResponse(rotated.SigningSecret, rotated.HasPendingSecretRotation)
        });
    }

    [HttpDelete("{endpointId:guid}")]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> Delete(Guid endpointId, CancellationToken cancellationToken)
        => await _managementService.DeleteEndpointAsync(_tenantContext.TenantId, endpointId, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "Webhook endpoint not found" });
}
