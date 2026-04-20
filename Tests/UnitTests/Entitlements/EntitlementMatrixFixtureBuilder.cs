using Domain.Entites;
using Domain.Authorization;

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

    public static IEnumerable<object[]> EndpointGateRegressionCases()
    {
        yield return
        [
            new EntitlementMatrixCase(
                Name: "billing_invoices_free_plan_denied_by_default",
                EntitlementKey: EntitlementKeys.BillingInvoicesRead,
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-free",
                PlanValue: "false",
                AddOns: [],
                OverrideValue: null,
                ExpectedResolvedValue: "false",
                ExpectedAllowed: false,
                ExpectedResolvedFrom: "Plan")
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "billing_invoices_free_plan_with_addon_allowed",
                EntitlementKey: EntitlementKeys.BillingInvoicesRead,
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-free",
                PlanValue: "false",
                AddOns:
                [
                    new EntitlementAddOnFixture("addon.billing.invoices.read", "true")
                ],
                OverrideValue: null,
                ExpectedResolvedValue: "true",
                ExpectedAllowed: true,
                ExpectedResolvedFrom: "AddOn")
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "billing_subscription_manage_plan_true_override_false_denied",
                EntitlementKey: EntitlementKeys.BillingSubscriptionManage,
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-growth",
                PlanValue: "true",
                AddOns: [],
                OverrideValue: "false",
                ExpectedResolvedValue: "false",
                ExpectedAllowed: false,
                ExpectedResolvedFrom: "Override",
                SubscriptionStatus: SubscriptionStatus.Active)
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "billing_plan_upgrade_plan_true_allowed_during_grace_period",
                EntitlementKey: EntitlementKeys.BillingPlanUpgrade,
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-pro",
                PlanValue: "true",
                AddOns: [],
                OverrideValue: null,
                ExpectedResolvedValue: "true",
                ExpectedAllowed: true,
                ExpectedResolvedFrom: "Plan",
                SubscriptionStatus: SubscriptionStatus.GracePeriod)
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "admin_advanced_user_management_addon_allow_then_override_deny",
                EntitlementKey: EntitlementKeys.AdminAdvancedUserManagement,
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-free",
                PlanValue: "false",
                AddOns:
                [
                    new EntitlementAddOnFixture("addon.admin.advanced.user-management", "true")
                ],
                OverrideValue: "false",
                ExpectedResolvedValue: "false",
                ExpectedAllowed: false,
                ExpectedResolvedFrom: "Override")
        ];

        yield return
        [
            new EntitlementMatrixCase(
                Name: "analytics_audit_logs_default_deny_then_override_allow_on_canceled_subscription",
                EntitlementKey: EntitlementKeys.AnalyticsAuditLogsRead,
                ValueType: EntitlementValueType.Boolean,
                DefinitionDefaultValue: "false",
                PlanId: "matrix-plan-free",
                PlanValue: "false",
                AddOns: [],
                OverrideValue: "true",
                ExpectedResolvedValue: "true",
                ExpectedAllowed: true,
                ExpectedResolvedFrom: "Override",
                SubscriptionStatus: SubscriptionStatus.Canceled)
        ];
    }
}
