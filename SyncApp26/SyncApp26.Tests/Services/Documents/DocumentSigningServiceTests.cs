using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Services.Documents
{
    public class DocumentSigningServiceTests
    {
        private readonly Mock<IDocumentService> _documentServiceMock = new();
        private readonly Mock<IDocumentSignatureService> _documentSignatureServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();

        private DocumentSigningService CreateService() =>
            new(_documentServiceMock.Object, _documentSignatureServiceMock.Object, _userServiceMock.Object);

        private static User MakeUser(Guid? id = null, Guid? assignedToId = null, string? email = null, UserRole role = UserRole.BasicUser) => new()
        {
            Id = id ?? Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Roe",
            Email = email ?? $"jane.roe.{Guid.NewGuid():N}@example.com",
            PersonalId = Guid.NewGuid().ToString(),
            AssignedToId = assignedToId,
            Role = role,
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

        // ───────────────────────── RequestSigningTokenAsync ─────────────────────────

        [Fact]
        public async Task RequestSigningTokenAsync_UnrelatedNonAdmin_ReturnsForbidden()
        {
            var service = CreateService();
            var caller = MakeUser();
            var document = MakeDocument(); // owned by someone else, no manager relation

            var result = await service.RequestSigningTokenAsync(document, caller, callerIsAdmin: false);

            Assert.True(result.Forbidden);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_UserAlreadySigned_Fails()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner);
            document.UserSignedAt = DateTime.UtcNow;

            var result = await service.RequestSigningTokenAsync(document, owner, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("already signed", result.ErrorMessage);
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
            Assert.Contains("Employee must sign first", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ManagerAlreadySigned_Fails()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            var document = MakeDocument(user: owner);
            document.ManagerSignedAt = DateTime.UtcNow;

            var result = await service.RequestSigningTokenAsync(document, manager, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("Manager already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_UserStatusMismatch_Fails()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingManager");

            var result = await service.RequestSigningTokenAsync(document, owner, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("User signature not required", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_ManagerStatusMismatch_Fails()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            var document = MakeDocument(user: owner, status: "PendingAdmin");
            document.UserSignedAt = DateTime.UtcNow;

            var result = await service.RequestSigningTokenAsync(document, manager, callerIsAdmin: false);

            Assert.False(result.Success);
            Assert.Contains("Manager signature not required", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_AdminWrongStatus_Fails()
        {
            var service = CreateService();
            var admin = MakeUser(role: UserRole.Admin);
            var document = MakeDocument(documentType: "SSM", status: "PendingManager");

            var result = await service.RequestSigningTokenAsync(document, admin, callerIsAdmin: true);

            Assert.False(result.Success);
            Assert.Contains("Admin signature not required", result.ErrorMessage);
        }

        [Fact]
        public async Task RequestSigningTokenAsync_AdminNonSsmDocument_Fails()
        {
            var service = CreateService();
            var admin = MakeUser(role: UserRole.Admin);
            var document = MakeDocument(documentType: "SU", status: "PendingAdmin");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;

            var result = await service.RequestSigningTokenAsync(document, admin, callerIsAdmin: true);

            Assert.False(result.Success);
            Assert.Contains("Admin only signs SSM", result.ErrorMessage);
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
        public async Task RequestSigningTokenAsync_ValidAdminSignature_ReturnsToken()
        {
            var service = CreateService();
            var admin = MakeUser(role: UserRole.Admin);
            var document = MakeDocument(documentType: "SSM", status: "PendingAdmin"); // unrelated owner
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
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
            var document = MakeDocument(user: owner);
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email, DocumentName = "SSM Document" };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);

            var result = await service.GetSigningContextAsync("tok");

            Assert.True(result.Success);
            Assert.True(result.IsManagerSigning);
            Assert.False(result.IsAdminSigning);
        }

        [Fact]
        public async Task GetSigningContextAsync_AdminSigningSsmDocument_ReturnsIsAdminSigningTrue()
        {
            var service = CreateService();
            var admin = MakeUser(email: "admin@example.com", role: UserRole.Admin);
            var document = MakeDocument(documentType: "SSM");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);

            var result = await service.GetSigningContextAsync("tok");

            Assert.True(result.Success);
            Assert.True(result.IsAdminSigning);
            Assert.False(result.IsManagerSigning);
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
        public async Task ConsumeSigningTokenAsync_UserAlreadySigned_Fails()
        {
            var service = CreateService();
            var owner = MakeUser();
            var document = MakeDocument(user: owner);
            document.UserSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = owner.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(owner.Email)).ReturnsAsync(owner);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ManagerBeforeEmployeeSigned_Fails()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingUser");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("Employee must sign", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ManagerAlreadySigned_Fails()
        {
            var service = CreateService();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner);
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("Manager already signed", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_AdminBothMustSignFirst_Fails()
        {
            var service = CreateService();
            var admin = MakeUser(role: UserRole.Admin);
            var document = MakeDocument(documentType: "SSM", status: "PendingAdmin");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = null;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("Both employee and manager must sign", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_AdminWrongDocumentType_Fails()
        {
            var service = CreateService();
            var admin = MakeUser(role: UserRole.Admin);
            var document = MakeDocument(documentType: "SU", status: "PendingAdmin");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("Admin only signs SSM", result.ErrorMessage);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_AdminWrongStatus_Fails()
        {
            var service = CreateService();
            var admin = MakeUser(role: UserRole.Admin);
            var document = MakeDocument(documentType: "SSM", status: "PendingManager");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.False(result.Success);
            Assert.Contains("not pending admin signature", result.ErrorMessage);
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
        public async Task ConsumeSigningTokenAsync_EmployeeSignature_Success_GeneratesManagerNotification()
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
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, owner.Id, true, "Draw", "data", "1.2.3.4", false, null), Times.Once);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_ManagerCountersign_Success_NoManagerNotification()
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
            Assert.Equal(1, result.TotalSigned);
            Assert.Null(result.ManagerEmail);
        }

        [Fact]
        public async Task ConsumeSigningTokenAsync_AdminSignature_Success()
        {
            var service = CreateService();
            var admin = MakeUser(role: UserRole.Admin);
            var document = MakeDocument(documentType: "SSM", status: "PendingAdmin");
            document.UserSignedAt = DateTime.UtcNow;
            document.ManagerSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = admin.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(admin.Email)).ReturnsAsync(admin);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);

            var result = await service.ConsumeSigningTokenAsync(new ConsumeSigningTokenRequest { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.True(result.Success);
            _documentServiceMock.Verify(s => s.UpdateDocumentSignatureAsync(document.Id, admin.Id, false, "Draw", "data", It.IsAny<string>(), true, null), Times.Once);
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
