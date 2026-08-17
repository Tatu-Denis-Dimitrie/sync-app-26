using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRoleAssignments",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleAssignments", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_AssignedByUserId",
                table: "UserRoleAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId_UserId",
                table: "UserRoleAssignments",
                columns: new[] { "RoleId", "UserId" });

            // Seed the fixed set of system roles code checks by name (SyncApp26.Domain.Constants.Roles).
            migrationBuilder.Sql(@"
                INSERT INTO Roles (Id, Name, Description, IsSystem, CreatedAt) VALUES
                    ('11111111-1111-1111-1111-111111111111', 'Admin', 'Application administrator', 1, CURRENT_TIMESTAMP),
                    ('22222222-2222-2222-2222-222222222222', 'LineManager', 'Manages direct reports', 1, CURRENT_TIMESTAMP),
                    ('33333333-3333-3333-3333-333333333333', 'BasicUser', 'Standard employee account', 1, CURRENT_TIMESTAMP),
                    ('44444444-4444-4444-4444-444444444444', 'SsmOfficer', 'Initiates and verifies SSM training sessions', 1, CURRENT_TIMESTAMP),
                    ('55555555-5555-5555-5555-555555555555', 'SuOfficer', 'Initiates and verifies SU training sessions', 1, CURRENT_TIMESTAMP);
            ");

            // Backfill: every existing user's current Role becomes an explicit role assignment. This
            // is the last statement that reads the legacy Users.Role column before it's dropped in a
            // later migration (DropUserRoleColumn) — verify assignment counts match user counts
            // before applying that one.
            migrationBuilder.Sql(@"
                INSERT INTO UserRoleAssignments (UserId, RoleId, AssignedAt, AssignedByUserId)
                SELECT u.Id, r.Id, CURRENT_TIMESTAMP, NULL
                FROM Users u
                JOIN Roles r ON r.Name = CASE u.Role WHEN 0 THEN 'Admin' WHEN 1 THEN 'LineManager' ELSE 'BasicUser' END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRoleAssignments");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
