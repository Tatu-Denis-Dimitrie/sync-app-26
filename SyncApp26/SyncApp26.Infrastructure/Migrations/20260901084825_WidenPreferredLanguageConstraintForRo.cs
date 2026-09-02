using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenPreferredLanguageConstraintForRo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
