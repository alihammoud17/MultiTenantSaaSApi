using System.Text.Json;
using Application.Services;
using Domain.DTOs;
using Domain.Interfaces;
using Domain.Outputs;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Controllers;

// Operator-only tenant status changes. Signed with TenantOperations:SharedSecret; there is no user identity here.
[ApiController]
[Route("api/internal/tenants")]
public class InternalTenantOperationsController : ControllerBase
{
    private const string SignatureHeaderName = "X-Operations-Signature";
    private const string TimestampHeaderName = "X-Operations-Timestamp";
    private const int MaxReasonLength = 500;
    private const int MaxActorLength = 100;

    private readonly ITenantLifecycleService _tenantLifecycleService;
    private readonly ITenantOperationsSignatureValidator _signatureValidator;

    public InternalTenantOperationsController(
        ITenantLifecycleService tenantLifecycleService,
        ITenantOperationsSignatureValidator signatureValidator)
    {
        _tenantLifecycleService = tenantLifecycleService;
        _signatureValidator = signatureValidator;
    }

    [HttpPost("{tenantId:guid}/suspend")]
    public Task<IActionResult> Suspend(Guid tenantId, CancellationToken cancellationToken)
    {
        return HandleAsync(tenantId, requireReason: true, (request, requestedAtUtc) =>
            _tenantLifecycleService.SuspendAsync(tenantId, requestedAtUtc, request.Reason!, request.Actor, cancellationToken), cancellationToken);
    }

    [HttpPost("{tenantId:guid}/reactivate")]
    public Task<IActionResult> Reactivate(Guid tenantId, CancellationToken cancellationToken)
    {
        return HandleAsync(tenantId, requireReason: false, (request, requestedAtUtc) =>
            _tenantLifecycleService.ReactivateAsync(tenantId, requestedAtUtc, request.Reason, request.Actor, cancellationToken), cancellationToken);
    }

    private async Task<IActionResult> HandleAsync(
        Guid tenantId,
        bool requireReason,
        Func<TenantOperationRequest, DateTime, Task<TenantStatusChangeResult>> change,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        var timestamp = Request.Headers[TimestampHeaderName].ToString();
        var signature = Request.Headers[SignatureHeaderName].ToString();

        if (!_signatureValidator.IsSignatureValid(payload, timestamp, signature) ||
            !HmacRequestSignature.TryParseTimestamp(timestamp, out var requestedAt))
        {
            return Unauthorized(new { error = "Invalid tenant operations signature" });
        }

        TenantOperationRequest? request;
        try
        {
            request = string.IsNullOrWhiteSpace(payload)
                ? new TenantOperationRequest(null, null)
                : JsonSerializer.Deserialize<TenantOperationRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid tenant operation payload" });
        }

        if (request is null)
            return BadRequest(new { error = "Invalid tenant operation payload" });

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var actor = string.IsNullOrWhiteSpace(request.Actor) ? null : request.Actor.Trim();

        if (requireReason && reason is null)
            return BadRequest(new { error = "Reason is required" });
        if (reason is { Length: > MaxReasonLength })
            return BadRequest(new { error = $"Reason must be at most {MaxReasonLength} characters" });
        if (actor is { Length: > MaxActorLength })
            return BadRequest(new { error = $"Actor must be at most {MaxActorLength} characters" });

        var result = await change(new TenantOperationRequest(reason, actor), requestedAt.UtcDateTime);

        return result.Outcome switch
        {
            TenantStatusChangeOutcome.Changed => Ok(new { tenantId, status = result.Status.ToString() }),
            TenantStatusChangeOutcome.TenantNotFound => NotFound(new { error = "Tenant not found" }),
            TenantStatusChangeOutcome.StaleRequest => Conflict(new { error = "Request is older than the tenant's last status change", status = result.Status.ToString() }),
            _ => Conflict(new { error = "Tenant status does not allow this operation", status = result.Status.ToString() })
        };
    }
}
