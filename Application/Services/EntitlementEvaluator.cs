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

    // Results already resolved in this HTTP request, keyed by tenant and entitlement key. The evaluator is scoped, so
    // this lives for one request; outside a request (no HttpContext) nothing is memoized and every call re-reads.
    private readonly Dictionary<(Guid TenantId, string EntitlementKey), EntitlementEvaluationResult> _requestResults = new();

    public EntitlementEvaluator(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<EntitlementEvaluationResult> EvaluateAsync(Guid tenantId, string entitlementKey, CancellationToken cancellationToken = default)
    {
        var results = await EvaluateManyAsync(tenantId, [entitlementKey], cancellationToken);
        return results[entitlementKey];
    }

    public async Task<IReadOnlyDictionary<string, EntitlementEvaluationResult>> EvaluateManyAsync(
        Guid tenantId,
        IEnumerable<string> entitlementKeys,
        CancellationToken cancellationToken = default)
    {
        var keys = entitlementKeys.Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<string, EntitlementEvaluationResult>();
        }

        if (_httpContextAccessor.HttpContext is null)
        {
            return await EvaluateFromDatabaseAsync(tenantId, keys, cancellationToken);
        }

        var results = new Dictionary<string, EntitlementEvaluationResult>(keys.Length, StringComparer.Ordinal);
        var missingKeys = new List<string>();
        foreach (var key in keys)
        {
            if (_requestResults.TryGetValue((tenantId, key), out var cached))
                results[key] = cached;
            else
                missingKeys.Add(key);
        }

        if (missingKeys.Count > 0)
        {
            foreach (var (key, result) in await EvaluateFromDatabaseAsync(tenantId, missingKeys.ToArray(), cancellationToken))
            {
                _requestResults[(tenantId, key)] = result;
                results[key] = result;
            }
        }

        return results;
    }

    private async Task<IReadOnlyDictionary<string, EntitlementEvaluationResult>> EvaluateFromDatabaseAsync(
        Guid tenantId,
        string[] keys,
        CancellationToken cancellationToken)
    {
        var evaluatedAtUtc = DateTime.UtcNow;
        var correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier;

        // One round trip for the subscription plus every requested key's plan value and active overrides. Both
        // collections are correlated to the subscription row (x.PlanId, x.TenantId) so they translate to joins; an
        // uncorrelated collection needs SQL APPLY, which SQLite (the test provider) cannot run.
        var subscriptionSnapshot = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new
            {
                x.Status,
                PlanValues = _dbContext.PlanEntitlements
                    .Where(pe => pe.PlanId == x.PlanId && keys.Contains(pe.EntitlementKey))
                    .Select(pe => new { pe.EntitlementKey, pe.Value })
                    .ToList(),
                Overrides = _dbContext.TenantEntitlementOverrides
                    .Where(o => o.TenantId == x.TenantId && keys.Contains(o.EntitlementKey))
                    .Where(o => o.EffectiveFromUtc <= evaluatedAtUtc)
                    .Where(o => o.EffectiveToUtc == null || o.EffectiveToUtc > evaluatedAtUtc)
                    .Select(o => new { o.EntitlementKey, o.EffectiveFromUtc, o.Value })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (subscriptionSnapshot is null)
        {
            return keys.ToDictionary(
                key => key,
                key => new EntitlementEvaluationResult(
                    tenantId,
                    key,
                    null,
                    false,
                    "DefaultDeny",
                    "MissingSubscription",
                    evaluatedAtUtc,
                    correlationId),
                StringComparer.Ordinal);
        }

        var definitions = await _dbContext.EntitlementDefinitions
            .AsNoTracking()
            .Where(x => keys.Contains(x.Key) && x.IsActive)
            .ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, cancellationToken);

        var addOnContributions = await _dbContext.TenantAddOnAssignments
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == TenantAddOnAssignmentStatus.Active)
            .Where(x => x.EffectiveFromUtc <= evaluatedAtUtc)
            .Where(x => x.EffectiveToUtc == null || x.EffectiveToUtc > evaluatedAtUtc)
            .Join(_dbContext.AddOnEntitlements.AsNoTracking().Where(x => keys.Contains(x.EntitlementKey)),
                assignment => assignment.AddOnId,
                addOnEntitlement => addOnEntitlement.AddOnId,
                (assignment, addOnEntitlement) => new
                {
                    assignment.Id,
                    addOnEntitlement.EntitlementKey,
                    addOnEntitlement.ValueMode,
                    addOnEntitlement.Value
                })
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var subscriptionStatus = subscriptionSnapshot.Status.ToString();
        var results = new Dictionary<string, EntitlementEvaluationResult>(keys.Length, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var definition = definitions.GetValueOrDefault(key);

            string? resolvedValue = definition?.DefaultValue;
            var resolvedFrom = definition?.DefaultValue is null ? "DefaultDeny" : "Default";

            var planValue = subscriptionSnapshot.PlanValues.FirstOrDefault(x => x.EntitlementKey == key)?.Value;
            if (!string.IsNullOrWhiteSpace(planValue))
            {
                resolvedValue = planValue;
                resolvedFrom = "Plan";
            }

            // Ordered by assignment id in the query; filtering keeps that order per key.
            foreach (var contribution in addOnContributions.Where(x => x.EntitlementKey == key))
            {
                resolvedValue = ApplyAddOnContribution(resolvedValue, contribution.Value, contribution.ValueMode, definition?.ValueType);
                resolvedFrom = "AddOn";
            }

            // Only the most recent active override counts, even when its value is blank.
            var overrideValue = subscriptionSnapshot.Overrides
                .Where(x => x.EntitlementKey == key)
                .MaxBy(x => x.EffectiveFromUtc)?
                .Value;
            if (!string.IsNullOrWhiteSpace(overrideValue))
            {
                resolvedValue = overrideValue;
                resolvedFrom = "Override";
            }

            results[key] = new EntitlementEvaluationResult(
                tenantId,
                key,
                resolvedValue,
                IsAllowed(resolvedValue, definition?.ValueType),
                resolvedFrom,
                subscriptionStatus,
                evaluatedAtUtc,
                correlationId);
        }

        return results;
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
