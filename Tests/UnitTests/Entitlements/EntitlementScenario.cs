using Domain.Entities;
using Infrastructure.Data;

namespace Tests.UnitTests.Entitlements;

/// <summary>
/// Seeds one tenant with a subscription to its own plan, plus definitions, plan values, add-ons and overrides for
/// entitlement keys unique to the scenario, so scenarios can share a database without seeing each other's rows.
/// </summary>
internal sealed class EntitlementScenario
{
    private readonly ApplicationDbContext _db;
    private readonly string _prefix = $"scn-{Guid.NewGuid():N}";
    private readonly string _assignmentIdPrefix = Guid.NewGuid().ToString("N")[..8];
    private int _addOnCount;

    private EntitlementScenario(ApplicationDbContext db, Tenant tenant, string planId)
    {
        _db = db;
        Tenant = tenant;
        PlanId = planId;
    }

    public Tenant Tenant { get; }

    public string PlanId { get; }

    public static EntitlementScenario Create(ApplicationDbContext db, SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Entitlement Tenant", Subdomain = $"ent-{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now };
        var planId = $"plan-{Guid.NewGuid():N}";

        db.Tenants.Add(tenant);
        db.Plans.Add(new Plan { Id = planId, Name = planId, MonthlyPrice = 0m, ApiCallsPerMonth = 1000, MaxUsers = 5, IsActive = true, DisplayOrder = 99 });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = planId,
            Status = status,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddMonths(1),
            CreatedAt = now
        });

        return new EntitlementScenario(db, tenant, planId);
    }

    /// <summary>Adds a definition and returns its scenario-unique key.</summary>
    public string Define(string name, EntitlementValueType valueType, string? defaultValue = null, bool isActive = true)
    {
        var key = $"{_prefix}.{name}";
        _db.EntitlementDefinitions.Add(new EntitlementDefinition
        {
            Key = key,
            DisplayName = key,
            ValueType = valueType,
            Category = valueType == EntitlementValueType.Boolean ? EntitlementCategory.Feature : EntitlementCategory.Quota,
            DefaultValue = defaultValue,
            IsActive = isActive
        });
        return key;
    }

    public EntitlementScenario PlanValue(string key, string value)
    {
        _db.PlanEntitlements.Add(new PlanEntitlement { PlanId = PlanId, EntitlementKey = key, Value = value });
        return this;
    }

    /// <summary>
    /// Assigns a new add-on contributing <paramref name="value"/> to <paramref name="key"/>. Assignment ids increase
    /// with each call, so contributions apply in call order.
    /// </summary>
    public EntitlementScenario AddOn(
        string key,
        AddOnEntitlementValueMode mode,
        string value,
        TenantAddOnAssignmentStatus status = TenantAddOnAssignmentStatus.Active,
        TimeSpan? startsIn = null,
        TimeSpan? endsIn = null)
    {
        var now = DateTime.UtcNow;
        var addOnId = $"{_prefix}.addon.{++_addOnCount}";
        _db.AddOnDefinitions.Add(new AddOnDefinition { Id = addOnId, DisplayName = addOnId, IsActive = true });
        _db.AddOnEntitlements.Add(new AddOnEntitlement { AddOnId = addOnId, EntitlementKey = key, ValueMode = mode, Value = value });
        _db.TenantAddOnAssignments.Add(new TenantAddOnAssignment
        {
            Id = new Guid($"{_assignmentIdPrefix}-0000-0000-0000-{_addOnCount:D12}"),
            TenantId = Tenant.Id,
            AddOnId = addOnId,
            Status = status,
            EffectiveFromUtc = now + (startsIn ?? TimeSpan.FromHours(-1)),
            EffectiveToUtc = endsIn is null ? null : now + endsIn
        });
        return this;
    }

    public EntitlementScenario Override(string key, string value, TimeSpan startedAgo, TimeSpan? endsIn = null, Guid? tenantId = null)
    {
        var now = DateTime.UtcNow;
        _db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? Tenant.Id,
            EntitlementKey = key,
            Value = value,
            Reason = "test",
            EffectiveFromUtc = now - startedAgo,
            EffectiveToUtc = endsIn is null ? null : now + endsIn
        });
        return this;
    }
}
