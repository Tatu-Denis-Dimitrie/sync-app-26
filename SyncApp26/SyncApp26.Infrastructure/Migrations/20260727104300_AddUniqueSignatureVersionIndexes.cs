using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueSignatureVersionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId_SignerRole_Version",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_SignatureRecords_Document_Role_Version",
                table: "SignatureRecords");

            migrationBuilder.DropIndex(
                name: "UX_SignatureRecords_Training_Role_Version",
                table: "SignatureRecords");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId_SignerRole_Version",
                table: "SignatureRecords",
                columns: new[] { "PeriodicTrainingId", "SignerRole", "Version" });
        }
    }
}
