using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Services.Documents
{
    // Workflow under test: User -> Manager (AssignedTo) -> Instructor -> Completed. The Instructor
    // slot belongs to whoever holds the SsmOfficer/SuOfficer role for the document's type — not a
    // per-row InstructorId match — and admin has no signing role anywhere in this chain.
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

        // Grants (or denies) the officer role that determines Instructor-step eligibility for a
        // given document type — this replaces the old per-row InstructorId match entirely.
        private void SetOfficer(User user, string documentType, bool isOfficer)
        {
            var role = documentType.Equals("SSM", StringComparison.OrdinalIgnoreCase) ? Roles.SsmOfficer : Roles.SuOfficer;
            _userServiceMock.Setup(s => s.IsInRoleAsync(user.Id, role)).ReturnsAsync(isOfficer);
        }

        // ───────────────────────── RequestSigningTokenAsync ─────────────────────────

        [Fact]
        public async Task RequestSigningTokenAsync_UnrelatedCaller_ReturnsForbidden()
        {
            // Not the owner, not the manager, not an officer for this document's type — being an
            // Admin elsewhere in the app grants no standing here at all.
            var service = CreateService();
            var caller = MakeUser();
            var document = MakeDocument(); // owned by someone else
            SetOfficer(caller, document.DocumentType!, false);

            var result = await service.RequestSigningTokenAsync(document, caller);

            Assert.True(result.Forbidden);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_UserAlreadySigned_Fails()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingUser");
            document.UserSignedAt = DateTime.UtcNow;
            SetOfficer(owner, document.DocumentType!, false);

            var result = await service.RequestSigningTokenAsync(document, owner);

            Assert.False(result.Success);
            Assert.Contains("User already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ValidUserSignature_ReturnsToken()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingUser");
            SetOfficer(owner, document.DocumentType!, false);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(owner.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("token-123");

            var result = await service.RequestSigningTokenAsync(document, owner);

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
            SetOfficer(manager, document.DocumentType!, false);

            var result = await service.RequestSigningTokenAsync(document, manager);

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
            SetOfficer(manager, document.DocumentType!, false);

            var result = await service.RequestSigningTokenAsync(document, manager);

            Assert.False(result.Success);
            Assert.Contains("Manager already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_OfficerTriesBeforeManagerTurn_Fails()
        {
            // The caller is the SSM officer for this document's type, but it isn't their turn yet —
            // Manager hasn't signed, so the document is still at PendingManager.
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            var document = MakeDocument(user: owner, status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            var officer = MakeUser();
            SetOfficer(officer, document.DocumentType!, true);

            var result = await service.RequestSigningTokenAsync(document, officer);

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
            SetOfficer(manager, document.DocumentType!, false);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(manager.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("manager-token");

            var result = await service.RequestSigningTokenAsync(document, manager);

            Assert.True(result.Success);
            Assert.Equal("manager-token", result.Token);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_CompletedDocument_Fails()
        {
            // Once the officer signs, the document is Completed and nobody — including the officer
            // who just signed it — can request another token for it.
            var service = CreateService();
            var officer = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "Completed");
            SetOfficer(officer, document.DocumentType!, true);

            var result = await service.RequestSigningTokenAsync(document, officer);

            Assert.False(result.Success);
            Assert.Contains("does not require a signature", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_EmployeeTriesDuringInstructorStep_Fails()
        {
            // The caller has a legitimate relation to the document (they're the employee) but it's
            // the officer's turn now, not theirs.
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            SetOfficer(owner, document.DocumentType!, false);

            var result = await service.RequestSigningTokenAsync(document, owner);

            Assert.False(result.Success);
            Assert.Contains("Instructor signature not required", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_AssignedManagerNotOfficer_Fails()
        {
            // Once a document reaches PendingInstructor, the employee's line manager (who already had
            // their own turn) is not automatically re-authorized as the officer too.
            var service = CreateService();
            var assignedManager = MakeUser();
            var owner = MakeUser(assignedToId: assignedManager.Id);
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            SetOfficer(assignedManager, document.DocumentType!, false);

            var result = await service.RequestSigningTokenAsync(document, assignedManager);

            Assert.False(result.Success);
            Assert.Contains("Manager already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ValidOfficerSignature_ReturnsToken()
        {
            var service = CreateService();
            var officer = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            SetOfficer(officer, document.DocumentType!, true);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(officer.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("officer-token");

            var result = await service.RequestSigningTokenAsync(document, officer);

            Assert.True(result.Success);
            Assert.Equal("officer-token", result.Token);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_SuOfficerCannotSignSsmDocument_ReturnsForbidden()
        {
            // Holding the officer role for one document type gives no standing on the other type.
            var service = CreateService();
            var suOfficer = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, documentType: "SSM", status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            _userServiceMock.Setup(s => s.IsInRoleAsync(suOfficer.Id, Roles.SsmOfficer)).ReturnsAsync(false);

            var result = await service.RequestSigningTokenAsync(document, suOfficer);

            Assert.True(result.Forbidden);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_AdminWithNoOtherRelation_ReturnsForbidden()
        {
            // Admin has no override anywhere in this chain — an admin unrelated to the document is
            // treated exactly like any other unrelated caller.
            var service = CreateService();
            var admin = MakeUser();
            var document = MakeDocument(status: "PendingManager"); // unrelated owner
            SetOfficer(admin, document.DocumentType!, false);

            var result = await service.RequestSigningTokenAsync(document, admin);

            Assert.True(result.Forbidden);
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
            SetOfficer(manager, document.DocumentType!, false);

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
            var officer = MakeUser(email: "officer@example.com");
            var owner = MakeUser(assignedToId: assignedManager.Id);
            owner.AssignedTo = assignedManager;
            var document = MakeDocument(user: owner, status: "PendingInstructor");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = officer.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(officer.Email)).ReturnsAsync(officer);
            SetOfficer(officer, document.DocumentType!, true);

            var result = await service.GetSigningContextAsync("tok");

            Assert.True(result.Success);
            Assert.True(result.IsInstructorSigning);
            Assert.False(result.IsManagerSigning);

            // The employee's line manager is a different account entirely and, once the document has
            // moved on to PendingInstructor, is not recognized as this step's signer either.
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(assignedManager.Email)).ReturnsAsync(assignedManager);
            SetOfficer(assignedManager, document.DocumentType!, false);
            var managerToken = new DocumentSignatureToken { DocumentId = document.Id, Email = assignedManager.Email };
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("mgr-tok")).ReturnsAsync(managerToken);
            var managerResult = await service.GetSigningContextAsync("mgr-tok");
            Assert.False(managerResult.IsInstructorSigning);
            Assert.False(managerResult.IsManagerSigning);
        }

        [Fact]
        public async Task GetSigningContextAsync_AdminNeverFlaggedAsSigning()
        {
            // IsAdminSigning is always false now — admin has no signing role in the chain at all.
            var service = CreateService();
            var admin = MakeUser(email: "admin@example.com");
            var document = MakeDocument(documentType: "SSM", status: "PendingInstructor");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);
            SetOfficer(admin, document.DocumentType!, false);

            var result = await service.GetSigningContextAsync("tok");

            Assert.True(result.Success);
            Assert.False(result.IsAdminSigning);
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
            SetOfficer(unrelated, document.DocumentType!, false);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("not awaiting your signature", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_AdminHasNoStandingOnAnyStep_Fails()
        {
            // Admin used to override Manager/Instructor on any document; that override is gone.
            var service = CreateService();
            var admin = MakeUser();
            var document = MakeDocument(documentType: "SSM", status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);
            SetOfficer(admin, document.DocumentType!, false);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("not awaiting your signature", result.ErrorMessage);
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
            SetOfficer(owner, document.DocumentType!, false);
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
            SetOfficer(owner, document.DocumentType!, false);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(manager.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("manager-tok");

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data", IpAddress = "1.2.3.4" });

            Assert.True(result.Success);
            Assert.Equal(1, result.TotalSigned);
            var notification = Assert.Single(result.NextSignerNotifications);
            Assert.Equal(manager.Email, notification.Email);
            Assert.Equal("manager-tok", notification.Token);
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
            SetOfficer(owner, document.DocumentType!, false);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            Assert.Empty(result.NextSignerNotifications);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ManagerCountersign_Success_NotifiesOfficer()
        {
            var service = CreateService();
            var manager = MakeUser();
            var officer = MakeUser(email: "officer@example.com");
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);
            SetOfficer(manager, document.DocumentType!, false);
            _userServiceMock.Setup(s => s.GetUsersInRoleAsync(Roles.SsmOfficer)).ReturnsAsync(new List<User> { officer });
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(officer.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("officer-tok");

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data", IpAddress = "1.2.3.4" });

            Assert.True(result.Success);
            var notification = Assert.Single(result.NextSignerNotifications);
            Assert.Equal(officer.Email, notification.Email);
            Assert.Equal("officer-tok", notification.Token);
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, manager.Id, "Manager", "Draw", "data", "1.2.3.4", null), Times.Once);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ManagerCountersign_NoOfficerFound_NoNotification()
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
            SetOfficer(manager, document.DocumentType!, false);
            _userServiceMock.Setup(s => s.GetUsersInRoleAsync(Roles.SsmOfficer)).ReturnsAsync(new List<User>());
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            Assert.Empty(result.NextSignerNotifications);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_OfficerSignature_Success_NoNotification()
        {
            var service = CreateService();
            var officer = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, documentType: "SU", status: "PendingInstructor");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = officer.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(officer.Email)).ReturnsAsync(officer);
            SetOfficer(officer, document.DocumentType!, true);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            Assert.Empty(result.NextSignerNotifications);
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, officer.Id, "Instructor", "Draw", "data", It.IsAny<string>(), null), Times.Once);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_SsmOfficerCannotSignSuDocument_Fails()
        {
            var service = CreateService();
            var ssmOfficer = MakeUser();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, documentType: "SU", status: "PendingInstructor");
            document.ManagerSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = ssmOfficer.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(ssmOfficer.Email)).ReturnsAsync(ssmOfficer);
            _userServiceMock.Setup(s => s.IsInRoleAsync(ssmOfficer.Id, Roles.SuOfficer)).ReturnsAsync(false);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("not awaiting your signature", result.ErrorMessage);
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
            SetOfficer(manager, document.DocumentType!, false);
            _userServiceMock.Setup(s => s.GetUsersInRoleAsync(Roles.SsmOfficer)).ReturnsAsync(new List<User>());
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);
            _documentServiceMock.Setup(s => s.BulkSignDocumentsAsync(manager.Id, "Draw", "data", It.IsAny<string>())).ReturnsAsync(3);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data", BulkSign = true });

            Assert.True(result.Success);
            Assert.Equal(4, result.TotalSigned); // 3 bulk-signed + 1 signed individually
            _documentServiceMock.Verify(s => s.BulkSignDocumentsAsync(manager.Id, "Draw", "data", It.IsAny<string>()), Times.Once);
        }
    }
}
