using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureAnomalyAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureAnomalyAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordsChecked = table.Column<int>(type: "INTEGER", nullable: false),
                    AnomaliesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReadByAdminId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureAnomalyAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureAnomalyAlerts_Users_ReadByAdminId",
                        column: x => x.ReadByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAnomalyAlerts_IsRead",
                table: "SignatureAnomalyAlerts",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAnomalyAlerts_OccurredAt",
                table: "SignatureAnomalyAlerts",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAnomalyAlerts_ReadByAdminId",
                table: "SignatureAnomalyAlerts",
                column: "ReadByAdminId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignatureAnomalyAlerts");
        }
    }
}
