using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoResolvedByImportHistoryIdToDataChangeRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AutoResolvedByImportHistoryId",
                table: "DataChangeRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataChangeRequests_AutoResolvedByImportHistoryId",
                table: "DataChangeRequests",
                column: "AutoResolvedByImportHistoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_DataChangeRequests_ImportHistories_AutoResolvedByImportHistoryId",
                table: "DataChangeRequests",
                column: "AutoResolvedByImportHistoryId",
                principalTable: "ImportHistories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DataChangeRequests_ImportHistories_AutoResolvedByImportHistoryId",
                table: "DataChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_DataChangeRequests_AutoResolvedByImportHistoryId",
                table: "DataChangeRequests");

            migrationBuilder.DropColumn(
                name: "AutoResolvedByImportHistoryId",
                table: "DataChangeRequests");
        }
    }
}
