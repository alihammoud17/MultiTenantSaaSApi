using Domain.DTOs;
using Domain.Entites;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PlansController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly IAuditService _auditService;

        public PlansController(
            ApplicationDbContext dbContext,
            ITenantContext tenantContext,
            IAuditService auditService)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _auditService = auditService;
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _dbContext.Plans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.MonthlyPrice,
                    p.ApiCallsPerMonth,
                    p.MaxUsers
                })
                .ToListAsync();

            return Ok(plans);
        }

        [HttpPost("upgrade")]
        [Authorize]
        public async Task<IActionResult> UpgradePlan([FromBody] UpgradePlanRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PlanId))
            {
                return BadRequest(new { error = "PlanId is required" });
            }

            var plan = await _dbContext.Plans
                .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive);

            if (plan == null)
            {
                return BadRequest(new { error = "Invalid plan id" });
            }

            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId);

            if (subscription == null)
            {
                return NotFound(new { error = "Subscription not found" });
            }

            if (subscription.PlanId == plan.Id)
            {
                return BadRequest(new { error = "Tenant is already on this plan" });
            }

            subscription.PlanId = plan.Id;
            subscription.Status = SubscriptionStatus.Active;
            subscription.CurrentPeriodStart = DateTime.UtcNow;
            subscription.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("TENANT_PLAN_CHANGED", nameof(Subscription), subscription.Id.ToString(), new
            {
                subscription.TenantId,
                subscription.PlanId
            });

            return Ok(new
            {
                message = "Plan updated",
                planId = subscription.PlanId,
                planName = plan.Name,
                plan.ApiCallsPerMonth,
                subscription.CurrentPeriodEnd
            });
        }
    }
}
