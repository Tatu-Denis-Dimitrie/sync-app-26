using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPreferredLanguageCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Model/snapshot say "IN (0, 1)" but no migration ever applied that - DB still had
            // the original "IN (0)", rejecting Language.Ro.
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PreferredLanguage",
                table: "Users");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PreferredLanguage",
                table: "Users",
                sql: "\"PreferredLanguage\" IS NULL OR \"PreferredLanguage\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PreferredLanguage",
                table: "Users");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PreferredLanguage",
                table: "Users",
                sql: "\"PreferredLanguage\" IS NULL OR \"PreferredLanguage\" IN (0)");
        }
    }
}
