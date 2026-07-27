using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicTrainingInstructorId : Migration
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

            migrationBuilder.AddForeignKey(
                name: "FK_PeriodicTrainings_Users_InstructorId",
                table: "PeriodicTrainings",
                column: "InstructorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PeriodicTrainings_Users_InstructorId",
                table: "PeriodicTrainings");

            migrationBuilder.DropIndex(
                name: "IX_PeriodicTrainings_InstructorId",
                table: "PeriodicTrainings");

            migrationBuilder.DropColumn(
                name: "InstructorId",
                table: "PeriodicTrainings");
        }
    }
}
