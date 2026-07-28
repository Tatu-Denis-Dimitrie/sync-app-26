using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicTrainingInstructorIdForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
