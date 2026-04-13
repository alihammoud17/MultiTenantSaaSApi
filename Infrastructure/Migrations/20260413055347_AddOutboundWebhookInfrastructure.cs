using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundWebhookInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContractVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    SourceEventKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantWebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CallbackUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SigningSecret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SubscribedEventTypes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantWebhookEndpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundWebhookDeliveries_OutboundWebhookEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "OutboundWebhookEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutboundWebhookDeliveries_TenantWebhookEndpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "TenantWebhookEndpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_EndpointId",
                table: "OutboundWebhookDeliveries",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_EventId_EndpointId",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "EventId", "EndpointId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_Status_NextAttemptAtUtc",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookEvents_EventId",
                table: "OutboundWebhookEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookEvents_SourceEventKey",
                table: "OutboundWebhookEvents",
                column: "SourceEventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookEvents_TenantId_EventType_OccurredAtUtc",
                table: "OutboundWebhookEvents",
                columns: new[] { "TenantId", "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantWebhookEndpoints_TenantId_Name",
                table: "TenantWebhookEndpoints",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboundWebhookDeliveries");

            migrationBuilder.DropTable(
                name: "OutboundWebhookEvents");

            migrationBuilder.DropTable(
                name: "TenantWebhookEndpoints");
        }
    }
}
