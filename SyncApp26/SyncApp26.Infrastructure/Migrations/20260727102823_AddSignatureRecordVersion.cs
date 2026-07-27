using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureRecordVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId",
                table: "SignatureRecords");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "SignatureRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId_SignerRole_Version",
                table: "SignatureRecords",
                columns: new[] { "PeriodicTrainingId", "SignerRole", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId_SignerRole_Version",
                table: "SignatureRecords");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "SignatureRecords");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId",
                table: "SignatureRecords",
                column: "PeriodicTrainingId");
        }
    }
}
