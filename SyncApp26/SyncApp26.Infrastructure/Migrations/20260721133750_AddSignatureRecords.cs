using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodicTrainingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SignerRole = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SignerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignerFullNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SignerPositionSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SignatureData = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialTaughtSnapshot = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DurationHoursSnapshot = table.Column<decimal>(type: "TEXT", nullable: true),
                    TrainingDateSnapshot = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PreviousSignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SignatureHmac = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsLegacyUnverified = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureRecords_PeriodicTrainings_PeriodicTrainingId",
                        column: x => x.PeriodicTrainingId,
                        principalTable: "PeriodicTrainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SignatureRecords_UserDocuments_UserDocumentId",
                        column: x => x.UserDocumentId,
                        principalTable: "UserDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SignatureRecords_Users_SignerUserId",
                        column: x => x.SignerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_PeriodicTrainingId",
                table: "SignatureRecords",
                column: "PeriodicTrainingId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_SignerUserId_SignedAt",
                table: "SignatureRecords",
                columns: new[] { "SignerUserId", "SignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRecords_UserDocumentId",
                table: "SignatureRecords",
                column: "UserDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignatureRecords");
        }
    }
}
