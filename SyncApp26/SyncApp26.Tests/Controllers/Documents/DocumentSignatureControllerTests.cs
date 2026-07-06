using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.API.Hubs;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Tests.TestHelpers;
using static SyncApp26.API.Controllers.DocumentSignatureController;

namespace SyncApp26.Tests.Controllers.Documents
{
    public class DocumentSignatureControllerTests
    {
        private readonly Mock<IDocumentSignatureService> _documentSignatureServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<IEmailService> _emailServiceMock = new();
        private readonly Mock<IDocumentService> _documentServiceMock = new();
        private readonly Mock<IConfiguration> _configurationMock = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<IHubContext<SyncHub>> _hubContextMock = new();
        private readonly Mock<IClientProxy> _clientProxyMock = new();

        public DocumentSignatureControllerTests()
        {
            var hubClientsMock = new Mock<IHubClients>();
            hubClientsMock.Setup(c => c.All).Returns(_clientProxyMock.Object);
            _hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
            _clientProxyMock.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), default)).Returns(Task.CompletedTask);
        }

        private DocumentSignatureController CreateController(Guid? callerId = null, string role = "Admin")
        {
            var controller = new DocumentSignatureController(
                _documentSignatureServiceMock.Object,
                _userServiceMock.Object,
                _emailServiceMock.Object,
                _documentServiceMock.Object,
                _configurationMock.Object,
                _scopeFactoryMock.Object,
                _hubContextMock.Object);

            controller.SetUser(callerId ?? Guid.NewGuid(), role: role);
            return controller;
        }

        private static User MakeUser(Guid? id = null, Guid? assignedToId = null, string? email = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Roe",
            Email = email ?? $"jane.roe.{Guid.NewGuid():N}@example.com",
            PersonalId = Guid.NewGuid().ToString(),
            AssignedToId = assignedToId,
            RoleId = Guid.NewGuid(),
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

        private static T GetProp<T>(object obj, string name) => (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;

        // ───────────────────────── RequestSignature ─────────────────────────

        [Fact]
        public async Task RequestSignature_MissingEmail_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.RequestSignature(new RequestSignatureDto { Email = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task RequestSignature_ExistingUser_SendsLoginEmail()
        {
            var controller = CreateController();
            var user = MakeUser(email: "existing@example.com");
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("existing@example.com")).ReturnsAsync(user);

            var result = await controller.RequestSignature(new RequestSignatureDto { Email = "existing@example.com", DocumentId = Guid.NewGuid(), DocumentName = "SSM" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailForRegisteredUserAsync(user.Email, "SSM", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RequestSignature_UnknownUser_SendsSecureLinkEmail()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync("new@example.com", It.IsAny<Guid>(), "SSM", null))
                .ReturnsAsync("tok");

            var result = await controller.RequestSignature(new RequestSignatureDto { Email = "new@example.com", DocumentId = Guid.NewGuid(), DocumentName = "SSM" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync("new@example.com", "SSM", It.IsAny<string>()), Times.Once);
        }

        // ───────────────────────── ValidateToken ─────────────────────────

        [Fact]
        public async Task ValidateToken_EmptyToken_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.ValidateToken("");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ValidateToken_InvalidToken_ReturnsBadRequest()
        {
            var controller = CreateController();
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync(It.IsAny<string>())).ReturnsAsync((DocumentSignatureToken?)null);

            var result = await controller.ValidateToken("bad-token");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ValidateToken_ManagerSigning_ReturnsIsManagerSigningTrue()
        {
            var controller = CreateController();
            var manager = MakeUser(email: "manager@example.com");
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner);
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email, DocumentName = "SSM Document" };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);

            var result = await controller.ValidateToken("tok");

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(GetProp<bool>(ok.Value!, "isManagerSigning"));
            Assert.False(GetProp<bool>(ok.Value!, "isAdminSigning"));
        }

        // ───────────────────────── ConsumeToken ─────────────────────────

        [Fact]
        public async Task ConsumeToken_MissingToken_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.ConsumeToken(new ConsumeTokenDto { Token = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ConsumeToken_InvalidToken_ReturnsBadRequest()
        {
            var controller = CreateController();
            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync(It.IsAny<string>())).ReturnsAsync((DocumentSignatureToken?)null);

            var result = await controller.ConsumeToken(new ConsumeTokenDto { Token = "bad" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ConsumeToken_UserAlreadySigned_ReturnsBadRequest()
        {
            var controller = CreateController();
            var owner = MakeUser();
            var document = MakeDocument(user: owner);
            document.UserSignedAt = DateTime.UtcNow;
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = owner.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(owner.Email)).ReturnsAsync(owner);

            var result = await controller.ConsumeToken(new ConsumeTokenDto { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("already signed", badRequest.Value!.ToString());
        }

        [Fact]
        public async Task ConsumeToken_ManagerBeforeEmployeeSigned_ReturnsBadRequest()
        {
            var controller = CreateController();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingUser");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = manager.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(manager.Email)).ReturnsAsync(manager);

            var result = await controller.ConsumeToken(new ConsumeTokenDto { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Employee must sign", badRequest.Value!.ToString());
        }

        [Fact]
        public async Task ConsumeToken_EmployeeSignature_Success_NotifiesManagerAndHub()
        {
            var controller = CreateController();
            var manager = MakeUser();
            var owner = MakeUser(assignedToId: manager.Id);
            owner.AssignedTo = manager;
            var document = MakeDocument(user: owner, status: "PendingUser");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = owner.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(owner.Email)).ReturnsAsync(owner);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(true);
            _documentServiceMock.Setup(s => s.UpdateDocumentSignatureAsync(
                document.Id, true, "Draw", "data", It.IsAny<string>(), false, null)).ReturnsAsync(true);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(manager.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("manager-tok");

            var result = await controller.ConsumeToken(new ConsumeTokenDto { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(manager.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _clientProxyMock.Verify(p => p.SendCoreAsync("SignatureUpdated", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task ConsumeToken_ConsumeFails_ReturnsBadRequest()
        {
            var controller = CreateController();
            var owner = MakeUser();
            var document = MakeDocument(user: owner, status: "PendingUser");
            var token = new DocumentSignatureToken { DocumentId = document.Id, Email = owner.Email };

            _documentSignatureServiceMock.Setup(s => s.ValidateTokenAsync("tok")).ReturnsAsync(token);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(owner.Email)).ReturnsAsync(owner);
            _documentSignatureServiceMock.Setup(s => s.ConsumeTokenAsync("tok")).ReturnsAsync(false);

            var result = await controller.ConsumeToken(new ConsumeTokenDto { Token = "tok", SignatureMethod = "Draw", SignatureData = "data" });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("could not be consumed", badRequest.Value!.ToString());
        }

        // ───────────────────────── BulkSign ─────────────────────────

        [Fact]
        public async Task BulkSign_MissingSignatureData_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.BulkSign(new BulkSignDto { SignatureData = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task BulkSign_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.BulkSign(new BulkSignDto { SignatureData = "data" });

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task BulkSign_NotAdminOrLineManager_ReturnsForbidden()
        {
            var controller = CreateController(role: "Basic User");

            var result = await controller.BulkSign(new BulkSignDto { SignatureData = "data" });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task BulkSign_Admin_ReturnsCountAndNotifiesHub()
        {
            var adminId = Guid.NewGuid();
            var controller = CreateController(adminId, role: "Admin");
            _documentServiceMock.Setup(s => s.BulkSignDocumentsAsync(true, adminId, "Draw", "data", It.IsAny<string>())).ReturnsAsync(4);

            var result = await controller.BulkSign(new BulkSignDto { SignatureMethod = "Draw", SignatureData = "data" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(4, GetProp<int>(ok.Value!, "count"));
            _clientProxyMock.Verify(p => p.SendCoreAsync("SignatureUpdated", It.IsAny<object[]>(), default), Times.Once);
        }

        // ───────────────────────── BulkSignAsync ─────────────────────────

        [Fact]
        public async Task BulkSignAsync_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.BulkSignAsync(new BulkSignDto { SignatureData = "data" });

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task BulkSignAsync_NoPendingDocuments_ReturnsOkWithNullJobId()
        {
            var controller = CreateController(role: "Admin");
            _documentServiceMock.Setup(s => s.GetPendingSsmDocumentsForAdminAsync()).ReturnsAsync(0);

            var result = await controller.BulkSignAsync(new BulkSignDto { SignatureData = "data" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Null(GetProp<string?>(ok.Value!, "jobId"));
        }

        [Fact]
        public async Task BulkSignAsync_PendingDocuments_StartsJobAndReturnsJobId()
        {
            var controller = CreateController(role: "Admin");
            _documentServiceMock.Setup(s => s.GetPendingSsmDocumentsForAdminAsync()).ReturnsAsync(3);

            var scopeMock = new Mock<IServiceScope>();
            var providerMock = new Mock<IServiceProvider>();
            providerMock.Setup(p => p.GetService(typeof(IDocumentService))).Returns(_documentServiceMock.Object);
            scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);
            _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
            _documentServiceMock.Setup(s => s.GetPendingSsmDocumentsForAdminListAsync()).ReturnsAsync(new List<UserDocument>());

            var result = await controller.BulkSignAsync(new BulkSignDto { SignatureMethod = "Draw", SignatureData = "data" });

            var ok = Assert.IsType<OkObjectResult>(result);
            var jobId = GetProp<string?>(ok.Value!, "jobId");
            Assert.False(string.IsNullOrEmpty(jobId));
            Assert.Equal(3, GetProp<int>(ok.Value!, "total"));
        }

        // ───────────────────────── GetBulkSignStatus ─────────────────────────

        [Fact]
        public void GetBulkSignStatus_UnknownJob_ReturnsNotFound()
        {
            var controller = CreateController(role: "Admin");

            var result = controller.GetBulkSignStatus(Guid.NewGuid().ToString());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        // ───────────────────────── AdminSignAndSendGeneratedDocuments ─────────────────────────

        [Fact]
        public async Task AdminSignAndSend_MissingDocumentType_ReturnsBadRequest()
        {
            var controller = CreateController(role: "Admin");

            var result = await controller.AdminSignAndSendGeneratedDocuments(new AdminSignAndSendDto { DocumentType = "", SignatureData = "data" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AdminSignAndSend_MissingSignatureData_ReturnsBadRequest()
        {
            var controller = CreateController(role: "Admin");

            var result = await controller.AdminSignAndSendGeneratedDocuments(new AdminSignAndSendDto { DocumentType = "SSM", SignatureData = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AdminSignAndSend_NonAdmin_ReturnsForbidden()
        {
            var controller = CreateController(role: "Basic User");

            var result = await controller.AdminSignAndSendGeneratedDocuments(new AdminSignAndSendDto { DocumentType = "SSM", SignatureData = "data" });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task AdminSignAndSend_Success_ReturnsCounts()
        {
            var controller = CreateController(role: "Admin");
            _documentServiceMock.Setup(s => s.BulkSignAndSendGeneratedDocumentsAsync("SSM", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(2);
            _documentServiceMock.Setup(s => s.GetAllPendingUserDocumentsAsync("SSM")).ReturnsAsync(Array.Empty<UserDocument>());

            var result = await controller.AdminSignAndSendGeneratedDocuments(new AdminSignAndSendDto { DocumentType = "SSM", SignatureMethod = "Draw", SignatureData = "data" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(2, GetProp<int>(ok.Value!, "documentsSigned"));
        }

        // ───────────────────────── GetPendingSsmAdminCount ─────────────────────────

        [Fact]
        public async Task GetPendingSsmAdminCount_ReturnsCount()
        {
            var controller = CreateController(role: "Admin");
            _documentServiceMock.Setup(s => s.GetPendingSsmDocumentsForAdminAsync()).ReturnsAsync(7);

            var result = await controller.GetPendingSsmAdminCount();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(7, GetProp<int>(ok.Value!, "count"));
        }
    }
}
