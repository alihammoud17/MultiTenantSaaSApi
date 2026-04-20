using Domain.Entites;

namespace Tests.UnitTests.Entitlements;

public sealed record EntitlementAddOnFixture(
    string AddOnId,
    string Value,
    AddOnEntitlementValueMode ValueMode = AddOnEntitlementValueMode.Set);

public sealed record EntitlementMatrixCase(
    string Name,
    string EntitlementKey,
    EntitlementValueType ValueType,
    string? DefinitionDefaultValue,
    string PlanId,
    string? PlanValue,
    IReadOnlyList<EntitlementAddOnFixture> AddOns,
    string? OverrideValue,
    string ExpectedResolvedValue,
    bool ExpectedAllowed,
    string ExpectedResolvedFrom,
    SubscriptionStatus SubscriptionStatus = SubscriptionStatus.Active)
{
    public override string ToString() => Name;
}
