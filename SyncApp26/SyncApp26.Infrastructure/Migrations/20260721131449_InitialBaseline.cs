using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
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
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentSignatureTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodicTrainingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DocumentName = table.Column<string>(type: "TEXT", nullable: false),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSignatureTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Functions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Functions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentFunctions",
                columns: table => new
                {
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FunctionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentFunctions", x => new { x.DepartmentId, x.FunctionId });
                    table.ForeignKey(
                        name: "FK_DepartmentFunctions_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepartmentFunctions_Functions_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "Functions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FunctionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignedToId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PersonalId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    IsEmailVerified = table.Column<bool>(type: "INTEGER", nullable: true),
                    EmailVerificationToken = table.Column<string>(type: "TEXT", nullable: true),
                    EmailVerificationTokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordResetTokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    BloodGroup = table.Column<string>(type: "TEXT", nullable: true),
                    BadgeNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Education = table.Column<string>(type: "TEXT", nullable: true),
                    Qualifications = table.Column<string>(type: "TEXT", nullable: true),
                    CommuteRoute = table.Column<string>(type: "TEXT", nullable: true),
                    CommuteDurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    AdmittedByName = table.Column<string>(type: "TEXT", nullable: true),
                    AdmittedByFunction = table.Column<string>(type: "TEXT", nullable: true),
                    AdmittedDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Role", "\"Role\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_Users_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Functions_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "Functions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_Users_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DataChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedChangesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedByAdminId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataChangeRequests_Users_ResolvedByAdminId",
                        column: x => x.ResolvedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DataChangeRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserChangeHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportHistoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NewValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserChangeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserChangeHistories_ImportHistories_ImportHistoryId",
                        column: x => x.ImportHistoryId,
                        principalTable: "ImportHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserChangeHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PdfFilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DocumentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserCryptographicSignature = table.Column<string>(type: "TEXT", nullable: true),
                    ManagerCryptographicSignature = table.Column<string>(type: "TEXT", nullable: true),
                    UserSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserSignatureData = table.Column<string>(type: "TEXT", nullable: true),
                    UserSignatureIpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserSignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ManagerSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ManagerSignatureData = table.Column<string>(type: "TEXT", nullable: true),
                    ManagerSignatureIpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ManagerSignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AdminCryptographicSignature = table.Column<string>(type: "TEXT", nullable: true),
                    AdminSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AdminSignatureData = table.Column<string>(type: "TEXT", nullable: true),
                    AdminSignatureIpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AdminSignedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDocuments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInitialTrainings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IntroductoryTrainingDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntroductoryTrainingHours = table.Column<int>(type: "INTEGER", nullable: true),
                    IntroductoryTrainingInstructor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IntroductoryTrainingInstructorFunction = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IntroductoryTrainingContent = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    WorkplaceTrainingDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WorkplaceTrainingLocation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WorkplaceTrainingHours = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkplaceTrainingInstructor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WorkplaceTrainingInstructorFunction = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WorkplaceTrainingContent = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    UserSignatureData = table.Column<string>(type: "TEXT", nullable: true),
                    UserSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    InstructorSignatureData = table.Column<string>(type: "TEXT", nullable: true),
                    InstructorSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    VerifierSignatureData = table.Column<string>(type: "TEXT", nullable: true),
                    VerifierSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInitialTrainings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInitialTrainings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSignatureHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignatureData = table.Column<string>(type: "TEXT", nullable: false),
                    SignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CryptographicProof = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PerformedByEmail = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSignatureHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSignatureHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignatureData = table.Column<string>(type: "TEXT", nullable: false),
                    SignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CryptographicProof = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSignatures_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PeriodicTrainings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    TrainingDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationHours = table.Column<decimal>(type: "TEXT", nullable: true),
                    Occupation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MaterialTaught = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    UserSignatureData = table.Column<string>(type: "TEXT", nullable: true),
                    UserSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    InstructorSignature = table.Column<string>(type: "TEXT", nullable: true),
                    InstructorSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    VerifierSignature = table.Column<string>(type: "TEXT", nullable: true),
                    VerifierSignatureMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    InstructorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    VerifierName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SourceRowId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodicTrainings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodicTrainings_UserDocuments_UserDocumentId",
                        column: x => x.UserDocumentId,
                        principalTable: "UserDocuments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PeriodicTrainings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataChangeRequests_ResolvedByAdminId",
                table: "DataChangeRequests",
                column: "ResolvedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_DataChangeRequests_Status",
                table: "DataChangeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DataChangeRequests_UserId",
                table: "DataChangeRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentFunctions_FunctionId",
                table: "DepartmentFunctions",
                column: "FunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Functions_Name",
                table: "Functions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodicTrainings_UserDocumentId",
                table: "PeriodicTrainings",
                column: "UserDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodicTrainings_UserId",
                table: "PeriodicTrainings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserChangeHistories_ImportHistoryId",
                table: "UserChangeHistories",
                column: "ImportHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserChangeHistories_UserId",
                table: "UserChangeHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDocuments_Status",
                table: "UserDocuments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_UserDocuments_UserId",
                table: "UserDocuments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInitialTrainings_UserId_DocumentType",
                table: "UserInitialTrainings",
                columns: new[] { "UserId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_AssignedToId",
                table: "Users",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DeletedAt",
                table: "Users",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId_DeletedAt",
                table: "Users",
                columns: new[] { "DepartmentId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_FunctionId",
                table: "Users",
                column: "FunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PersonalId",
                table: "Users",
                column: "PersonalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSignatureHistories_CreatedAt",
                table: "UserSignatureHistories",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignatureHistories_UserId",
                table: "UserSignatureHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignatures_UserId",
                table: "UserSignatures",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataChangeRequests");

            migrationBuilder.DropTable(
                name: "DepartmentFunctions");

            migrationBuilder.DropTable(
                name: "DocumentSignatureTokens");

            migrationBuilder.DropTable(
                name: "PeriodicTrainings");

            migrationBuilder.DropTable(
                name: "UserChangeHistories");

            migrationBuilder.DropTable(
                name: "UserInitialTrainings");

            migrationBuilder.DropTable(
                name: "UserSignatureHistories");

            migrationBuilder.DropTable(
                name: "UserSignatures");

            migrationBuilder.DropTable(
                name: "UserDocuments");

            migrationBuilder.DropTable(
                name: "ImportHistories");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Functions");
        }
    }
}
