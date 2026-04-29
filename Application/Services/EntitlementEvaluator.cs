using System.Globalization;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class EntitlementEvaluator : IEntitlementEvaluator
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public EntitlementEvaluator(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<EntitlementEvaluationResult> EvaluateAsync(Guid tenantId, string entitlementKey, CancellationToken cancellationToken = default)
    {
        var evaluatedAtUtc = DateTime.UtcNow;
        var correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier;

        var subscription = await _dbContext.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        if (subscription is null)
        {
            return new EntitlementEvaluationResult(
                tenantId,
                entitlementKey,
                null,
                false,
                "DefaultDeny",
                "MissingSubscription",
                evaluatedAtUtc,
                correlationId);
        }

        var definition = await _dbContext.EntitlementDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Key == entitlementKey && x.IsActive, cancellationToken);

        string? resolvedValue = definition?.DefaultValue;
        var resolvedFrom = definition?.DefaultValue is null ? "DefaultDeny" : "Default";

        var planValue = await _dbContext.PlanEntitlements
            .AsNoTracking()
            .Where(x => x.PlanId == subscription.PlanId && x.EntitlementKey == entitlementKey)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(planValue))
        {
            resolvedValue = planValue;
            resolvedFrom = "Plan";
        }

        var addOnContributions = await _dbContext.TenantAddOnAssignments
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == TenantAddOnAssignmentStatus.Active)
            .Where(x => x.EffectiveFromUtc <= evaluatedAtUtc)
            .Where(x => x.EffectiveToUtc == null || x.EffectiveToUtc > evaluatedAtUtc)
            .Join(_dbContext.AddOnEntitlements.AsNoTracking().Where(x => x.EntitlementKey == entitlementKey),
                assignment => assignment.AddOnId,
                addOnEntitlement => addOnEntitlement.AddOnId,
                (assignment, addOnEntitlement) => new
                {
                    assignment.Id,
                    addOnEntitlement.ValueMode,
                    addOnEntitlement.Value
                })
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var contribution in addOnContributions)
        {
            resolvedValue = ApplyAddOnContribution(resolvedValue, contribution.Value, contribution.ValueMode, definition?.ValueType);
            resolvedFrom = "AddOn";
        }

        var overrideValue = await _dbContext.TenantEntitlementOverrides
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EntitlementKey == entitlementKey)
            .Where(x => x.EffectiveFromUtc <= evaluatedAtUtc)
            .Where(x => x.EffectiveToUtc == null || x.EffectiveToUtc > evaluatedAtUtc)
            .OrderByDescending(x => x.EffectiveFromUtc)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            resolvedValue = overrideValue;
            resolvedFrom = "Override";
        }

        var isAllowed = IsAllowed(resolvedValue, definition?.ValueType);

        return new EntitlementEvaluationResult(
            tenantId,
            entitlementKey,
            resolvedValue,
            isAllowed,
            resolvedFrom,
            subscription.Status.ToString(),
            evaluatedAtUtc,
            correlationId);
    }

    private static string? ApplyAddOnContribution(
        string? baseValue,
        string contributionValue,
        AddOnEntitlementValueMode valueMode,
        EntitlementValueType? valueType)
    {
        if (valueType is not (EntitlementValueType.Integer or EntitlementValueType.Decimal))
        {
            return contributionValue;
        }

        if (!decimal.TryParse(baseValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var baseDecimal))
        {
            baseDecimal = 0m;
        }

        if (!decimal.TryParse(contributionValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var contributionDecimal))
        {
            contributionDecimal = 0m;
        }

        var merged = valueMode switch
        {
            AddOnEntitlementValueMode.Increment => baseDecimal + contributionDecimal,
            AddOnEntitlementValueMode.Max => Math.Max(baseDecimal, contributionDecimal),
            AddOnEntitlementValueMode.Min => Math.Min(baseDecimal, contributionDecimal),
            _ => contributionDecimal
        };

        return merged.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsAllowed(string? value, EntitlementValueType? valueType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (valueType == EntitlementValueType.Boolean || valueType is null)
        {
            return bool.TryParse(value, out var enabled) && enabled;
        }

        return true;
    }
}
