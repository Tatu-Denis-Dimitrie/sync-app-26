using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingInstructorSignatureColumnsToUserDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns were always part of the UserDocument entity and model snapshot, but were
            // never actually added to the UserDocuments CreateTable in the initial baseline migration -
            // so a fresh database never had them, even though EF's model believed it did.
            migrationBuilder.AddColumn<string>(
                name: "InstructorCryptographicSignature",
                table: "UserDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructorSignatureMethod",
                table: "UserDocuments",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructorSignatureData",
                table: "UserDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructorSignatureIpAddress",
                table: "UserDocuments",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InstructorSignedAt",
                table: "UserDocuments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstructorCryptographicSignature",
                table: "UserDocuments");

            migrationBuilder.DropColumn(
                name: "InstructorSignatureMethod",
                table: "UserDocuments");

            migrationBuilder.DropColumn(
                name: "InstructorSignatureData",
                table: "UserDocuments");

            migrationBuilder.DropColumn(
                name: "InstructorSignatureIpAddress",
                table: "UserDocuments");

            migrationBuilder.DropColumn(
                name: "InstructorSignedAt",
                table: "UserDocuments");
        }
    }
}
