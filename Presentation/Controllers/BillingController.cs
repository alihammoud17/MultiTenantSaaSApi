using Domain.Authorization;
using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Presentation.Authorization;

namespace Presentation.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IEntitlementEnforcer _entitlementEnforcer;

    public BillingController(
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IEntitlementEnforcer entitlementEnforcer)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _entitlementEnforcer = entitlementEnforcer;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var tenantId = _tenantContext.TenantId;

        var subscription = await _dbContext.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .SingleOrDefaultAsync(s => s.TenantId == tenantId);

        if (subscription is null)
        {
            return NotFound(new { error = "Subscription not found" });
        }

        var availableActions = BuildAvailableActions(subscription.Status);

        return Ok(new BillingStatusResponse(
            subscription.PlanId,
            subscription.Plan.Name,
            subscription.Status.ToString(),
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            subscription.ScheduledPlanEffectiveAtUtc,
            subscription.ScheduledPlanId,
            subscription.GracePeriodEndsAtUtc,
            subscription.CanceledAtUtc,
            availableActions));
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> ListInvoices()
    {
        var tenantId = _tenantContext.TenantId;
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, tenantId, EntitlementKeys.BillingInvoicesRead);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        var invoices = await _dbContext.BillingEventInboxes
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EventType.StartsWith("invoice."))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new BillingInvoiceListItemResponse(
                x.Id,
                x.ProviderEventId,
                x.SubscriptionId,
                0m,
                0m,
                "USD",
                x.EventType == "invoice.payment_failed" ? "Failed" : "Processed",
                x.OccurredAtUtc,
                x.EffectiveAtUtc,
                x.EventType == "invoice.payment_failed" ? null : x.EffectiveAtUtc))
            .ToListAsync();

        return Ok(invoices);
    }

    [HttpPost("subscription/cancel")]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> CancelSubscription([FromBody] CancelSubscriptionRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, tenantId, EntitlementKeys.BillingSubscriptionManage);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        var subscription = await _dbContext.Subscriptions.SingleOrDefaultAsync(s => s.TenantId == tenantId);
        if (subscription is null)
        {
            return NotFound(new { error = "Subscription not found" });
        }

        if (subscription.Status == SubscriptionStatus.Canceled)
        {
            return BadRequest(new { error = "Subscription is already canceled" });
        }

        subscription.Status = SubscriptionStatus.Canceled;
        subscription.CanceledAtUtc = DateTime.UtcNow;
        subscription.ScheduledPlanId = null;
        subscription.ScheduledPlanEffectiveAtUtc = null;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync("TENANT_SUBSCRIPTION_CANCELED", nameof(Subscription), subscription.Id.ToString(), new
        {
            subscription.TenantId,
            request.Reason
        });

        return Ok(new { message = "Subscription canceled", status = subscription.Status.ToString() });
    }

    [HttpPost("subscription/reactivate")]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IActionResult> ReactivateSubscription([FromBody] ReactivateSubscriptionRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        var deniedResult = await this.EnforceFeatureAsync(_entitlementEnforcer, tenantId, EntitlementKeys.BillingSubscriptionManage);
        if (deniedResult is not null)
        {
            return deniedResult;
        }

        var subscription = await _dbContext.Subscriptions.SingleOrDefaultAsync(s => s.TenantId == tenantId);
        if (subscription is null)
        {
            return NotFound(new { error = "Subscription not found" });
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.CanceledAtUtc = null;
        subscription.GracePeriodEndsAtUtc = null;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync("TENANT_SUBSCRIPTION_REACTIVATED", nameof(Subscription), subscription.Id.ToString(), new
        {
            subscription.TenantId,
            request.Reason
        });

        return Ok(new { message = "Subscription reactivated", status = subscription.Status.ToString() });
    }

    private static IReadOnlyCollection<string> BuildAvailableActions(SubscriptionStatus status)
    {
        return status switch
        {
            SubscriptionStatus.Canceled => ["reactivate"],
            SubscriptionStatus.Expired => ["reactivate"],
            _ => ["cancel", "change_plan"]
        };
    }
}
