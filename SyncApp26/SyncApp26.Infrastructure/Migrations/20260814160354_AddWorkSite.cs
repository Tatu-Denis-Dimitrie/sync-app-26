using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkSiteId",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_WorkSiteId",
                table: "Users",
                column: "WorkSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSites_Name",
                table: "WorkSites",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_WorkSites_WorkSiteId",
                table: "Users",
                column: "WorkSiteId",
                principalTable: "WorkSites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_WorkSites_WorkSiteId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "WorkSites");

            migrationBuilder.DropIndex(
                name: "IX_Users_WorkSiteId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WorkSiteId",
                table: "Users");
        }
    }
}
