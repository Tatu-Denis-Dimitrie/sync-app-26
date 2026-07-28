using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepurposeSignatureVersionAsHmacSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_SignatureRecords_Document_Role_Version",
                table: "SignatureRecords");

            migrationBuilder.DropIndex(
                name: "UX_SignatureRecords_Training_Role_Version",
                table: "SignatureRecords");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId_SignerRole",
                table: "SignatureRecords",
                columns: new[] { "PeriodicTrainingId", "SignerRole" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_UserDocumentId_SignerRole",
                table: "SignatureRecords",
                columns: new[] { "UserDocumentId", "SignerRole" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId_SignerRole",
                table: "SignatureRecords");

            migrationBuilder.DropIndex(
                name: "IX_SignatureRecords_UserDocumentId_SignerRole",
                table: "SignatureRecords");

            migrationBuilder.CreateIndex(
                name: "UX_SignatureRecords_Document_Role_Version",
                table: "SignatureRecords",
                columns: new[] { "UserDocumentId", "SignerRole", "Version" },
                unique: true,
                filter: "\"PeriodicTrainingId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SignatureRecords_Training_Role_Version",
                table: "SignatureRecords",
                columns: new[] { "PeriodicTrainingId", "SignerRole", "Version" },
                unique: true,
                filter: "\"PeriodicTrainingId\" IS NOT NULL");
        }
    }
}
