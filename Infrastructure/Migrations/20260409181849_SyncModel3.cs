using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AddOnDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BillingProviderProductRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntitlementDefinitions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ValueType = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultValue = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitlementDefinitions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TenantAddOnAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddOnId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAddOnAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantAddOnAssignments_AddOnDefinitions_AddOnId",
                        column: x => x.AddOnId,
                        principalTable: "AddOnDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantAddOnAssignments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AddOnEntitlements",
                columns: table => new
                {
                    AddOnId = table.Column<string>(type: "character varying(120)", nullable: false),
                    EntitlementKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ValueMode = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOnEntitlements", x => new { x.AddOnId, x.EntitlementKey });
                    table.ForeignKey(
                        name: "FK_AddOnEntitlements_AddOnDefinitions_AddOnId",
                        column: x => x.AddOnId,
                        principalTable: "AddOnDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AddOnEntitlements_EntitlementDefinitions_EntitlementKey",
                        column: x => x.EntitlementKey,
                        principalTable: "EntitlementDefinitions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanEntitlements",
                columns: table => new
                {
                    PlanId = table.Column<string>(type: "text", nullable: false),
                    EntitlementKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlements", x => new { x.PlanId, x.EntitlementKey });
                    table.ForeignKey(
                        name: "FK_PlanEntitlements_EntitlementDefinitions_EntitlementKey",
                        column: x => x.EntitlementKey,
                        principalTable: "EntitlementDefinitions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanEntitlements_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantEntitlementOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntitlementKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEntitlementOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantEntitlementOverrides_EntitlementDefinitions_Entitleme~",
                        column: x => x.EntitlementKey,
                        principalTable: "EntitlementDefinitions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantEntitlementOverrides_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "EntitlementDefinitions",
                columns: new[] { "Key", "Category", "CreatedUtc", "DefaultValue", "Description", "DisplayName", "IsActive", "UpdatedUtc", "ValueType" },
                values: new object[,]
                {
                    { "feature.billing.invoices.read", 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "false", "Allows tenant users to read tenant-scoped billing invoice summaries.", "Billing invoice list access", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { "quota.api.calls.monthly", 1, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "0", "Plan-level monthly API call quota baseline.", "Monthly API call quota", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { "quota.users.max", 1, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "1", "Plan-level max allowed tenant users.", "Maximum tenant users", true, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 1 }
                });

            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "EntitlementKey", "PlanId", "CreatedUtc", "Source", "UpdatedUtc", "Value" },
                values: new object[,]
                {
                    { "feature.billing.invoices.read", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "quota.api.calls.monthly", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "1000" },
                    { "quota.users.max", "plan-free", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "1" },
                    { "feature.billing.invoices.read", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "quota.api.calls.monthly", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "50000" },
                    { "quota.users.max", "plan-pro", new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 4, 9, 0, 0, 0, 0, DateTimeKind.Utc), "10" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddOnEntitlements_EntitlementKey",
                table: "AddOnEntitlements",
                column: "EntitlementKey");

            migrationBuilder.CreateIndex(
                name: "IX_PlanEntitlements_EntitlementKey",
                table: "PlanEntitlements",
                column: "EntitlementKey");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAddOnAssignments_AddOnId",
                table: "TenantAddOnAssignments",
                column: "AddOnId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAddOnAssignments_TenantId_AddOnId_Status",
                table: "TenantAddOnAssignments",
                columns: new[] { "TenantId", "AddOnId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantAddOnAssignments_TenantId_Status_EffectiveFromUtc",
                table: "TenantAddOnAssignments",
                columns: new[] { "TenantId", "Status", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantEntitlementOverrides_EntitlementKey",
                table: "TenantEntitlementOverrides",
                column: "EntitlementKey");

            migrationBuilder.CreateIndex(
                name: "IX_TenantEntitlementOverrides_TenantId_EntitlementKey_Effectiv~",
                table: "TenantEntitlementOverrides",
                columns: new[] { "TenantId", "EntitlementKey", "EffectiveFromUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddOnEntitlements");

            migrationBuilder.DropTable(
                name: "PlanEntitlements");

            migrationBuilder.DropTable(
                name: "TenantAddOnAssignments");

            migrationBuilder.DropTable(
                name: "TenantEntitlementOverrides");

            migrationBuilder.DropTable(
                name: "AddOnDefinitions");

            migrationBuilder.DropTable(
                name: "EntitlementDefinitions");
        }
    }
}
