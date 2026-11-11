using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260501090000_AddWebhookEndpointManagementFoundation")]
    public partial class AddWebhookEndpointManagementFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NextSigningSecret",
                table: "TenantWebhookEndpoints",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextSigningSecretIssuedAtUtc",
                table: "TenantWebhookEndpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SigningSecretIssuedAtUtc",
                table: "TenantWebhookEndpoints",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NextSigningSecret", table: "TenantWebhookEndpoints");
            migrationBuilder.DropColumn(name: "NextSigningSecretIssuedAtUtc", table: "TenantWebhookEndpoints");
            migrationBuilder.DropColumn(name: "SigningSecretIssuedAtUtc", table: "TenantWebhookEndpoints");
        }
    }
}
