using Domain.Entites;

namespace Tests.UnitTests.Entitlements;

public static class EntitlementMatrixFixtureBuilder
{
    public static readonly DateTime BaselineUtc = new(2026, 04, 01, 0, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<object[]> BooleanCases()
    {
        yield return
        [
            new EntitlementMatrixCase(
                Name: "plan_only_true",
                EntitlementKey: "matrix.feature.billing.invoices.read",
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-free",
                PlanValue: "true",
                AddOns: [],
                OverrideValue: null,
                ExpectedResolvedValue: "true",
                ExpectedAllowed: true,
                ExpectedResolvedFrom: "Plan")
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "plan_false_addon_set_true",
                EntitlementKey: "matrix.feature.billing.invoices.read",
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-free",
                PlanValue: "false",
                AddOns:
                [
                    new EntitlementAddOnFixture("addon.invoice-access", "true")
                ],
                OverrideValue: null,
                ExpectedResolvedValue: "true",
                ExpectedAllowed: true,
                ExpectedResolvedFrom: "AddOn")
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "plan_true_addon_true_override_false",
                EntitlementKey: "matrix.feature.billing.invoices.read",
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-pro",
                PlanValue: "true",
                AddOns:
                [
                    new EntitlementAddOnFixture("addon.invoice-access", "true")
                ],
                OverrideValue: "false",
                ExpectedResolvedValue: "false",
                ExpectedAllowed: false,
                ExpectedResolvedFrom: "Override")
        ];
    }

    public static IEnumerable<object[]> NumericCases()
    {
        yield return
        [
            new EntitlementMatrixCase(
                Name: "plan_base_plus_increment_addon",
                EntitlementKey: "matrix.quota.projects.max",
                ValueType: EntitlementValueType.Integer,
                DefinitionDefaultValue: "1",
                PlanId: "matrix-plan-growth",
                PlanValue: "5",
                AddOns:
                [
                    new EntitlementAddOnFixture("addon.extra-projects-3", "3", AddOnEntitlementValueMode.Increment)
                ],
                OverrideValue: null,
                ExpectedResolvedValue: "8",
                ExpectedAllowed: true,
                ExpectedResolvedFrom: "AddOn")
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "plan_increment_then_override",
                EntitlementKey: "matrix.quota.projects.max",
                ValueType: EntitlementValueType.Integer,
                DefinitionDefaultValue: "1",
                PlanId: "matrix-plan-growth",
                PlanValue: "5",
                AddOns:
                [
                    new EntitlementAddOnFixture("addon.extra-projects-3", "3", AddOnEntitlementValueMode.Increment)
                ],
                OverrideValue: "2",
                ExpectedResolvedValue: "2",
                ExpectedAllowed: true,
                ExpectedResolvedFrom: "Override")
        ];
    }
}
