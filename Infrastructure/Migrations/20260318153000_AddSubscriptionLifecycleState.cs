using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    public partial class AddSubscriptionLifecycleState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAtUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodEndsAtUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduledPlanId",
                table: "Subscriptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledPlanEffectiveAtUtc",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveAtUtc",
                table: "BillingEventInboxes",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanceledAtUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "GracePeriodEndsAtUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ScheduledPlanId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ScheduledPlanEffectiveAtUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "EffectiveAtUtc",
                table: "BillingEventInboxes");
        }
    }
}
