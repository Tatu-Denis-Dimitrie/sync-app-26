using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicTrainingInstructorIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InstructorId",
                table: "PeriodicTrainings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodicTrainings_InstructorId",
                table: "PeriodicTrainings",
                column: "InstructorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PeriodicTrainings_InstructorId",
                table: "PeriodicTrainings");

            migrationBuilder.DropColumn(
                name: "InstructorId",
                table: "PeriodicTrainings");
        }
    }
}
