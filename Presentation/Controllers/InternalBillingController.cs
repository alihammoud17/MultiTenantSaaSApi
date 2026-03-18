using System.Text.Json;
using Domain.DTOs;
using Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Controllers;

[ApiController]
[Route("api/internal/billing")]
public class InternalBillingController : ControllerBase
{
    private const string SignatureHeaderName = "X-Billing-Signature";
    private const string TimestampHeaderName = "X-Billing-Timestamp";
    private readonly IBillingCallbackProcessor _billingCallbackProcessor;
    private readonly IInternalRequestSignatureValidator _signatureValidator;

    public InternalBillingController(
        IBillingCallbackProcessor billingCallbackProcessor,
        IInternalRequestSignatureValidator signatureValidator)
    {
        _billingCallbackProcessor = billingCallbackProcessor;
        _signatureValidator = signatureValidator;
    }

    [HttpPost("subscription-events")]
    public async Task<IActionResult> PostSubscriptionEvent(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();

        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var timestamp = Request.Headers[TimestampHeaderName].ToString();
        var signature = Request.Headers[SignatureHeaderName].ToString();

        if (!_signatureValidator.IsSignatureValid(payload, timestamp, signature))
        {
            return Unauthorized(new { error = "Invalid internal billing signature" });
        }

        BillingCallbackRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BillingCallbackRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid billing callback payload" });
        }

        if (request is null)
        {
            return BadRequest(new { error = "Invalid billing callback payload" });
        }

        try
        {
            var result = await _billingCallbackProcessor.ProcessAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
