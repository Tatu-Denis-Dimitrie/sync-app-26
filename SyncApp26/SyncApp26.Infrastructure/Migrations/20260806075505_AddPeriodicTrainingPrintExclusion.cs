using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicTrainingPrintExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExcludedFromPrintAt",
                table: "PeriodicTrainings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExcludedFromPrintById",
                table: "PeriodicTrainings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedFromPrintAt",
                table: "PeriodicTrainings");

            migrationBuilder.DropColumn(
                name: "ExcludedFromPrintById",
                table: "PeriodicTrainings");
        }
    }
}
