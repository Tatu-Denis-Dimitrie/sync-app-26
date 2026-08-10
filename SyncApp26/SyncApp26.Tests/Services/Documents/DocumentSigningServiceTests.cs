using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Services.Documents
{
    // Workflow under test: User -> Manager (AssignedTo) -> Instructor (training.InstructorId) ->
    // Admin (SSM only) / Completed (SU, after Instructor). Manager and Instructor are independent
    // roles/steps — dispatch in DocumentSigningService is by document.Status, not by which role
    // flags happen to be true for the caller (the same person could hold more than one role).
    public class DocumentSigningServiceTests
    {
        private readonly Mock<IDocumentService> _documentServiceMock = new();
        private readonly Mock<IDocumentSignatureService> _documentSignatureServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();

        private DocumentSigningService CreateService() =>
            new(_documentServiceMock.Object, _documentSignatureServiceMock.Object, _userServiceMock.Object);

        private static User MakeUser(Guid? id = null, Guid? assignedToId = null, string? email = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Roe",
            Email = email ?? $"jane.roe.{Guid.NewGuid():N}@example.com",
            PersonalId = Guid.NewGuid().ToString(),
            AssignedToId = assignedToId,
            CreatedAt = DateTime.UtcNow
        };

        private static UserDocument MakeDocument(Guid? id = null, User? user = null, string documentType = "SSM", string status = "PendingUser")
        {
            var owner = user ?? MakeUser();
            return new UserDocument
            {
                Id = id ?? Guid.NewGuid(),
                UserId = owner.Id,
                User = owner,
                DocumentType = documentType,
                Status = status
            };
        }

        // Attaches a PeriodicTraining row to the document's owner, linked via UserDocumentId —
        // mirrors how DocumentService.FindTargetPeriodicTraining/ResolveInstructorId locate it.
        private static PeriodicTraining MakeTraining(UserDocument doc, Guid? instructorId, DateTime? trainingDate = null)
        {
            var training = new PeriodicTraining
            {
                Id = Guid.NewGuid(),
                UserId = doc.UserId,
                UserDocumentId = doc.Id,
                InstructorId = instructorId,
                TrainingDate = trainingDate ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            doc.User!.PeriodicTrainings.Add(training);
            return training;
        }

        // ───────────────────────── RequestSigningTokenAsync ─────────────────────────

        [Fact]
        public async Task RequestSigningTokenAsync_UnrelatedNonAdmin_ReturnsForbidden()
        {
            var service = CreateService();
            var caller = MakeUser();
            var document = MakeDocument(); // owned by someone else, no manager/instructor relation

            var result = await service.RequestSigningTokenAsync(document, caller, callerIsAdmin: false);

            Assert.True(result.Forbidden);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_UserAlreadySigned_Fails()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingUser");
            document.UserSignedAt = DateTime.UtcNow;

            var result = await service.RequestSigningTokenAsync(document, owner, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("User already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ValidUserSignature_ReturnsToken()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingUser");
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(owner.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("token-123");

            var result = await service.RequestSigningTokenAsync(document, owner, callerIsAdmin: false);

            Assert.True(result.Success);
            Assert.Equal("token-123", result.Token);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ManagerBeforeEmployeeSigned_Fails()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            var document = MakeDocument(user: owner, status: "PendingUser");

            var result = await service.RequestSigningTokenAsync(document, manager, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("User signature not required", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ManagerAlreadySigned_Fails()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;

            var result = await service.RequestSigningTokenAsync(document, manager, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("Manager already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_LinkedInstructorTriesBeforeManagerTurn_Fails()
        {
            // The caller has a legitimate relation to the document (they're the linked
            // instructor) but it isn't their turn yet — Manager hasn't signed, so the document
            // is still at PendingManager, not PendingInstructor.
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            var document = MakeDocument(user: owner, status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            var instructor = MakeUser();
            MakeTraining(document, instructorId: instructor.Id);

            var result = await service.RequestSigningTokenAsync(document, instructor, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("Manager signature not required", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ValidManagerCountersign_ReturnsToken()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            var document = MakeDocument(user: owner, status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(manager.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("manager-token");

            var result = await service.RequestSigningTokenAsync(document, manager, callerIsAdmin: false);

            Assert.True(result.Success);
            Assert.Equal("manager-token", result.Token);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_InstructorAlreadySigned_Fails()
        {
            var service = CreateService();
            var instructor = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingAdmin");
            document.InstructorSignedAt = DateTime.UtcNow;
            MakeTraining(document, instructorId: instructor.Id);

            var result = await service.RequestSigningTokenAsync(document, instructor, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("Instructor already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_EmployeeTriesDuringInstructorStep_Fails()
        {
            // The caller has a legitimate relation to the document (they're the employee) but
            // it's the instructor's turn now, not theirs.
            var service = CreateService();
            var instructor = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            MakeTraining(document, instructorId: instructor.Id);

            var result = await service.RequestSigningTokenAsync(document, owner, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("Instructor signature not required", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_AssignedManagerNotLinkedInstructor_Fails()
        {
            // Once a document reaches PendingInstructor, the employee's line manager (who already
            // had their own turn) is not automatically re-authorized as the instructor too.
            var service = CreateService();
            var assignedManager = MakeUser();
            var instructor = MakeUser();
            var owner = MakeUser(assignedToId: assignedManager.Id);
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            MakeTraining(document, instructorId: instructor.Id);

            var result = await service.RequestSigningTokenAsync(document, assignedManager, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("Manager already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ValidInstructorSignature_ReturnsToken()
        {
            var service = CreateService();
            var instructor = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            MakeTraining(document, instructorId: instructor.Id);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(instructor.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("instructor-token");

            var result = await service.RequestSigningTokenAsync(document, instructor, callerIsAdmin: false);

            Assert.True(result.Success);
            Assert.Equal("instructor-token", result.Token);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_AdminWrongStatus_Fails()
        {
            var service = CreateService();
            var admin = MakeUser();
            var document = MakeDocument(documentType: "SSM", status: "Completed");

            var result = await service.RequestSigningTokenAsync(document, admin, callerIsAdmin: true);

            Assert.False(result.Success);
            Assert.Contains("does not require a signature", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_AdminNonSsmDocument_Fails()
        {
            var service = CreateService();
            var admin = MakeUser();
            var document = MakeDocument(documentType: "SU", status: "PendingAdmin");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            document.InstructorSignedAt = DateTime.UtcNow;

            var result = await service.RequestSigningTokenAsync(document, admin, callerIsAdmin: true);

            Assert.False(result.Success);
            Assert.Contains("Admin only signs SSM", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ValidAdminSignature_ReturnsToken()
        {
            var service = CreateService();
            var admin = MakeUser();
            var document = MakeDocument(documentType: "SSM", status: "PendingAdmin"); // unrelated owner
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            document.InstructorSignedAt = DateTime.UtcNow;
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(admin.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("admin-token");

            var result = await service.RequestSigningTokenAsync(document, admin, callerIsAdmin: true);

            Assert.True(result.Success);
            Assert.Equal("admin-token", result.Token);
        }

        // ───────────────────────── GetSigningContextAsync ─────────────────────────

        [Fact]
        public async Task GetSigningContextAsync_EmptyToken_Fails()
        {
            var service = CreateService();

            var result = await service.GetSigningContextAsync("");

            Assert.False(result.Success);
            Assert.Equal("Token is required.", result.ErrorMessage);
        }

        [Fact]
        public async Task GetSigningContextAsync_InvalidToken_Fails()
        {
            var service = CreateService();
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync(It.IsAny<string>())).ReturnsAsync((DocumentSignatureToken?)null);

            var result = await service.GetSigningContextAsync("bad-token");

            Assert.False(result.Success);
            Assert.Equal("Invalid or expired token.", result.ErrorMessage);
        }

        [Fact]
        public async Task GetSigningContextAsync_ManagerSigning_ReturnsIsManagerSigningTrue()
        {
            var service = CreateService();
            var manager = MakeUser(email: "manager@example.com");
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingManager");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email, DocumentName = "SSM Document" };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);

            var result = await service.GetSigningContextAsync("tok");

            Assert.True(result.Success);
            Assert.True(result.IsManagerSigning);
            Assert.False(result.IsInstructorSigning);
            Assert.False(result.IsAdminSigning);
        }

        [Fact]
        public async Task GetSigningContextAsync_InstructorSigning_ReturnsIsInstructorSigningTrue_NotManager()
        {
            var service = CreateService();
            var assignedManager = MakeUser(email: "manager@example.com");
            var instructor = MakeUser(email: "instructor@example.com");
            var owner = MakeUser(assignedToId: assignedManager.Id);
            owner.AssignedTo = assignedManager;
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            var training = MakeTraining(document, instructorId: instructor.Id);
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = instructor.Email, PeriodicTrainingId = training.Id };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(instructor.Email)).ReturnsAsync(instructor);

            var result = await service.GetSigningContextAsync("tok");

            Assert.True(result.Success);
            Assert.True(result.IsInstructorSigning);
            Assert.False(result.IsManagerSigning);

            // The employee's line manager is a different account entirely and, once the document
            // has moved on to PendingInstructor, is not recognized as this step's signer either.
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(assignedManager.Email)).ReturnsAsync(assignedManager);
            var managerToken = new DocumentSignatureToken { DocumentId = document.Id, Email = assignedManager.Email, PeriodicTrainingId = training.Id };
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("mgr-tok")).ReturnsAsync(managerToken);
            var managerResult = await service.GetSigningContextAsync("mgr-tok");
            Assert.False(managerResult.IsInstructorSigning);
            Assert.False(managerResult.IsManagerSigning);
        }

        [Fact]
        public async Task GetSigningContextAsync_AdminSigningSsmDocument_ReturnsIsAdminSigningTrue()
        {
            var service = CreateService();
            var admin = MakeUser(email: "admin@example.com");
            var document = MakeDocument(documentType: "SSM", status: "PendingAdmin");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);
            _userServiceMock.Setup(s => s.IsInRoleAsync(admin.Id, Roles.Admin)).ReturnsAsync(true);

            var result = await service.GetSigningContextAsync("tok");

            Assert.True(result.Success);
            Assert.True(result.IsAdminSigning);
            Assert.False(result.IsManagerSigning);
            Assert.False(result.IsInstructorSigning);
        }

        // ───────────────────────── ConsumeSigningTokenAsync ─────────────────────────

        [Fact]
        public async Task ConsumeSigningTokenAsync_EmptyToken_Fails()
        {
            var service = CreateService();

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "" });

            Assert.False(result.Success);
            Assert.Equal("Token is required.", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_InvalidToken_Fails()
        {
            var service = CreateService();
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync(It.IsAny<string>())).ReturnsAsync((DocumentSignatureToken?)null);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "bad" });

            Assert.False(result.Success);
            Assert.Equal("Token is invalid or expired.", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_DocumentNotFound_Fails()
        {
            var service = CreateService();
            var token = new DocumentSignatureToken { DocumentId = Guid.NewGuid(), Email = "a@b.com" };
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(token.DocumentId)).ReturnsAsync((UserDocument?)null);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok" });

            Assert.False(result.Success);
            Assert.Equal("Document not found.", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_SignerAccountNotFound_Fails()
        {
            var service = CreateService();
            var document = MakeDocument();
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = "ghost@example.com" };
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("ghost@example.com")).ReturnsAsync((User?)null);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok" });

            Assert.False(result.Success);
            Assert.Equal("Signer account not found.", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_NotAwaitingCallersSignature_Fails()
        {
            var service = CreateService();
            var unrelated = MakeUser();
            var document = MakeDocument(status: "PendingManager"); // owned by someone else
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = unrelated.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(unrelated.Email)).ReturnsAsync(unrelated);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("not awaiting your signature", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_AdminWrongDocumentType_Fails()
        {
            var service = CreateService();
            var admin = MakeUser();
            var document = MakeDocument(documentType: "SU", status: "PendingAdmin");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);
            _userServiceMock.Setup(s => s.IsInRoleAsync(admin.Id, Roles.Admin)).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("Admin only signs SSM", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ConsumeFails_Fails()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingUser");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = owner.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(owner.Email)).ReturnsAsync(owner);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(false);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("could not be consumed", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_EmployeeSignature_Success_NotifiesManager()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingUser");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = owner.Email, PeriodicTrainingId = null };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(owner.Email)).ReturnsAsync(owner);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(manager.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("manager-tok");

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data", IpAddress = "1.2.3.4" });

            Assert.True(result.Success);
            Assert.Equal(1, result.TotalSigned);
            Assert.Equal(manager.Email, result.ManagerEmail);
            Assert.Equal("manager-tok", result.ManagerNotificationToken);
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, owner.Id, "User", "Draw", "data", "1.2.3.4", null), Times.Once);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_EmployeeSignature_NoAssignedManager_NoNotification()
        {
            var service = CreateService();
            var owner = MakeUser(); // no AssignedTo
            var document = MakeDocument(user: owner, status: "PendingUser");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = owner.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(owner.Email)).ReturnsAsync(owner);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            Assert.Null(result.ManagerEmail);
            Assert.Null(result.ManagerNotificationToken);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ManagerCountersign_Success_NotifiesLinkedInstructor()
        {
            var service = CreateService();
            var manager = MakeUser();
            var instructor = MakeUser(email: "instructor@example.com");
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            var training = MakeTraining(document, instructorId: instructor.Id);
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email, PeriodicTrainingId = training.Id };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(instructor.Id)).ReturnsAsync(instructor);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(instructor.Email, document.Id, It.IsAny<string>(), training.Id))
                .ReturnsAsync("instructor-tok");

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data", IpAddress = "1.2.3.4" });

            Assert.True(result.Success);
            Assert.Equal(instructor.Email, result.ManagerEmail);
            Assert.Equal("instructor-tok", result.ManagerNotificationToken);
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, manager.Id, "Manager", "Draw", "data", "1.2.3.4", training.Id), Times.Once);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ManagerCountersign_NoLinkedInstructor_NoNotification()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            Assert.Null(result.ManagerEmail);
            Assert.Null(result.ManagerNotificationToken);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_InstructorSignature_Success_NoNotification()
        {
            var service = CreateService();
            var instructor = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, documentType: "SU", status: "PendingInstructor");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            MakeTraining(document, instructorId: instructor.Id);
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = instructor.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(instructor.Email)).ReturnsAsync(instructor);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            Assert.Null(result.ManagerEmail);
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, instructor.Id, "Instructor", "Draw", "data", It.IsAny<string>(), null), Times.Once);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_AdminSignature_Success()
        {
            var service = CreateService();
            var admin = MakeUser();
            var document = MakeDocument(documentType: "SSM", status: "PendingAdmin");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);
            _userServiceMock.Setup(s => s.IsInRoleAsync(admin.Id, Roles.Admin)).ReturnsAsync(true);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, admin.Id, "Admin", "Draw", "data", It.IsAny<string>(), null), Times.Once);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_BulkSignRequested_ReportsCombinedCount()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);
            _documentServiceMock.Setup(s => s.BulkSignDocumentsAsync(false, manager.Id, "Draw", "data", It.IsAny<string>())).ReturnsAsync(3);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data", BulkSign = true });

            Assert.True(result.Success);
            Assert.Equal(4, result.TotalSigned); // 3 bulk-signed + 1 signed individually
            _documentServiceMock.Verify(s => s.BulkSignDocumentsAsync(false, manager.Id, "Draw", "data", It.IsAny<string>()), Times.Once);
        }
    }
}
