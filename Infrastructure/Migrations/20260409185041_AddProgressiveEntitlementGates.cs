using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressiveEntitlementGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "EntitlementDefinitions",
                columns: new[] { "Key", "Category", "CreatedUtc", "DefaultValue", "Description", "DisplayName", "IsActive", "UpdatedUtc", "ValueType" },
                values: new object[,]
                {
                    { "feature.admin.advanced.user_management", 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "false", "Allows privileged tenant administrators to access advanced user management capabilities.", "Advanced tenant user management", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { "feature.analytics.audit_logs.read", 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "false", "Allows tenant users to access analytics-oriented audit log read surfaces.", "Tenant analytics and audit visibility", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { "feature.billing.plan.upgrade", 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "false", "Allows tenant billing managers to perform plan upgrades.", "Billing plan upgrade flow", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { "feature.billing.subscription.manage", 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "false", "Allows cancellation and reactivation actions for tenant subscriptions.", "Billing subscription self-service management", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { "feature.modules.future.hooks", 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "false", "Reserved module-level gate to support progressive rollout for future protected modules.", "Future module gating hook", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "EntitlementKey", "PlanId", "CreatedUtc", "Source", "UpdatedUtc", "Value" },
                values: new object[,]
                {
                    { "feature.admin.advanced.user_management", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.analytics.audit_logs.read", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.billing.plan.upgrade", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.billing.subscription.manage", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.modules.future.hooks", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.admin.advanced.user_management", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.analytics.audit_logs.read", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.billing.plan.upgrade", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.billing.subscription.manage", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.modules.future.hooks", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.admin.advanced.user_management", "plan-free" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.analytics.audit_logs.read", "plan-free" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.billing.plan.upgrade", "plan-free" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.billing.subscription.manage", "plan-free" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.modules.future.hooks", "plan-free" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.admin.advanced.user_management", "plan-pro" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.analytics.audit_logs.read", "plan-pro" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.billing.plan.upgrade", "plan-pro" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.billing.subscription.manage", "plan-pro" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.modules.future.hooks", "plan-pro" });

            migrationBuilder.DeleteData(
                table: "EntitlementDefinitions",
                keyColumn: "Key",
                keyValue: "feature.admin.advanced.user_management");

            migrationBuilder.DeleteData(
                table: "EntitlementDefinitions",
                keyColumn: "Key",
                keyValue: "feature.analytics.audit_logs.read");

            migrationBuilder.DeleteData(
                table: "EntitlementDefinitions",
                keyColumn: "Key",
                keyValue: "feature.billing.plan.upgrade");

            migrationBuilder.DeleteData(
                table: "EntitlementDefinitions",
                keyColumn: "Key",
                keyValue: "feature.billing.subscription.manage");

            migrationBuilder.DeleteData(
                table: "EntitlementDefinitions",
                keyColumn: "Key",
                keyValue: "feature.modules.future.hooks");
        }
    }
}
