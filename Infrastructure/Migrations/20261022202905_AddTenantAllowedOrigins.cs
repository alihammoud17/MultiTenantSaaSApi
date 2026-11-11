using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAllowedOrigins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantAllowedOrigins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Origin = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAllowedOrigins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantAllowedOrigins_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "EntitlementDefinitions",
                columns: new[] { "Key", "Category", "CreatedUtc", "DefaultValue", "Description", "DisplayName", "IsActive", "UpdatedUtc", "ValueType" },
                values: new object[] { "feature.security.cors_origins.manage", 0, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), "false", "Allows tenant administrators to register the browser origins allowed to call the API cross-origin.", "Tenant CORS origin management", true, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.InsertData(
                table: "PlanEntitlements",
                columns: new[] { "EntitlementKey", "PlanId", "CreatedUtc", "Source", "UpdatedUtc", "Value" },
                values: new object[,]
                {
                    { "feature.security.cors_origins.manage", "plan-free", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "feature.security.cors_origins.manage", "plan-pro", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), "true" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantAllowedOrigins_TenantId_Origin",
                table: "TenantAllowedOrigins",
                columns: new[] { "TenantId", "Origin" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantAllowedOrigins");

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.security.cors_origins.manage", "plan-free" });

            migrationBuilder.DeleteData(
                table: "PlanEntitlements",
                keyColumns: new[] { "EntitlementKey", "PlanId" },
                keyValues: new object[] { "feature.security.cors_origins.manage", "plan-pro" });

            migrationBuilder.DeleteData(
                table: "EntitlementDefinitions",
                keyColumn: "Key",
                keyValue: "feature.security.cors_origins.manage");
        }
    }
}
