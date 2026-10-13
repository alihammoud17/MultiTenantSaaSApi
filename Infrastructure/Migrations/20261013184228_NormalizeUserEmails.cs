using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeUserEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Emails are now stored in canonical lowercase form (Domain.Identity.EmailAddress.Normalize).
            // If two users differ only in case, the first UPDATE violates a unique email index (IX_Users_Email, or
            // IX_Users_TenantId_Email within one tenant) and the migration rolls back:
            // resolve those rows by hand first (see "Email normalization check" in docs/operations.md).
            migrationBuilder.Sql(@"UPDATE ""Users"" SET ""Email"" = lower(""Email"") WHERE ""Email"" <> lower(""Email"");");
            migrationBuilder.Sql(@"UPDATE ""UserInvites"" SET ""Email"" = lower(""Email"") WHERE ""Email"" <> lower(""Email"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the original casing is not kept, so it cannot be restored.
        }
    }
}
