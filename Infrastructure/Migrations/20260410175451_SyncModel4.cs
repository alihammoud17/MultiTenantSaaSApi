using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaEnabledAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecret",
                table: "Users",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserMfaEnrollmentChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMfaEnrollmentChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserStepUpSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStepUpSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaEnrollmentChallenges_EnrollmentTokenHash",
                table: "UserMfaEnrollmentChallenges",
                column: "EnrollmentTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaEnrollmentChallenges_TenantId_UserId_ExpiresAt",
                table: "UserMfaEnrollmentChallenges",
                columns: new[] { "TenantId", "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserStepUpSessions_SessionTokenHash",
                table: "UserStepUpSessions",
                column: "SessionTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserStepUpSessions_TenantId_UserId_Purpose_ExpiresAt",
                table: "UserStepUpSessions",
                columns: new[] { "TenantId", "UserId", "Purpose", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserMfaEnrollmentChallenges");

            migrationBuilder.DropTable(
                name: "UserStepUpSessions");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaEnabledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaSecret",
                table: "Users");
        }
    }
}
