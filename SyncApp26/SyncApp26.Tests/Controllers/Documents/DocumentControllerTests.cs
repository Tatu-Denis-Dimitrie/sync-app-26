using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Tests.TestHelpers;
using static SyncApp26.API.Controllers.DocumentController;

namespace SyncApp26.Tests.Controllers.Documents
{
    public class DocumentControllerTests
    {
        private readonly Mock<IDocumentService> _documentServiceMock = new();
        private readonly Mock<IEmailService> _emailServiceMock = new();
        private readonly Mock<IDocumentSignatureService> _documentSignatureServiceMock = new();
        private readonly Mock<IDocumentSigningService> _documentSigningServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<ISignatureVerificationService> _signatureVerificationServiceMock = new();
        private readonly Mock<IConfiguration> _configurationMock = new();

        private DocumentController CreateController(Guid? callerId = null, string role = Roles.Admin)
        {
            _signatureVerificationServiceMock
                .Setup(s => s.GetLatestSignatureRecordIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, SyncApp26.Shared.DTOs.Response.SignatureVerification.DocumentSignatureIdsDTO>());

            var controller = new DocumentController(
                _documentServiceMock.Object,
                _emailServiceMock.Object,
                _documentSignatureServiceMock.Object,
                _documentSigningServiceMock.Object,
                _userServiceMock.Object,
                _signatureVerificationServiceMock.Object,
                _configurationMock.Object);

            controller.SetUser(callerId ?? Guid.NewGuid(), role: role);
            return controller;
        }

        private static User MakeUser(Guid? id = null, Guid? assignedToId = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Roe",
            Email = $"jane.roe.{Guid.NewGuid():N}@example.com",
            PersonalId = Guid.NewGuid().ToString(),
            AssignedToId = assignedToId,
            Role = UserRole.BasicUser,
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

        // ───────────────────────── BulkGenerateDocuments ─────────────────────────

        [Fact]
        public async Task BulkGenerateDocuments_MissingDocumentType_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task BulkGenerateDocuments_Admin_GeneratesBothTypesAndSendsEmails()
        {
            var controller = CreateController(role: Roles.Admin);
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>()))
                .ReturnsAsync((2, 1));
            _documentServiceMock.Setup(s => s.GetAllPendingUserDocumentsAsync(It.IsAny<string>()))
                .ReturnsAsync(Array.Empty<UserDocument>());

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "Both" });

            var ok = Assert.IsType<OkObjectResult>(result);
            _documentServiceMock.Verify(s => s.BulkGenerateDocumentsAsync("SSM", It.IsAny<string>(), null, null), Times.Once);
            _documentServiceMock.Verify(s => s.BulkGenerateDocumentsAsync("SU", It.IsAny<string>(), null, null), Times.Once);
        }

        [Fact]
        public async Task BulkGenerateDocuments_NonAdmin_PassesCallerIdAsRestriction()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.LineManager);
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>()))
                .ReturnsAsync((1, 0));
            _documentServiceMock.Setup(s => s.GetAllPendingUserDocumentsAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<UserDocument>());

            var request = new BulkGenerateDocumentDto { DocumentType = "SSM" };
            var result = await controller.BulkGenerateDocuments(request);

            Assert.IsType<OkObjectResult>(result);
            _documentServiceMock.Verify(s => s.BulkGenerateDocumentsAsync("SSM", It.IsAny<string>(), request.SelectedUserIds, callerId), Times.Once);
        }

        // ───────────────────────── GenerateDocument ─────────────────────────

        [Fact]
        public async Task GenerateDocument_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.GenerateDocument(new GenerateDocumentDto { UserId = Guid.NewGuid(), DocumentType = "SSM" });

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GenerateDocument_NonAdminNotManager_ReturnsForbidden()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.LineManager);
            var user = MakeUser(); // not assigned to caller
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await controller.GenerateDocument(new GenerateDocumentDto { UserId = user.Id, DocumentType = "SSM" });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GenerateDocument_Success_SendsSignatureEmail()
        {
            var controller = CreateController(role: Roles.Admin);
            var user = MakeUser();
            var document = MakeDocument(user: user);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);
            _documentServiceMock.Setup(s => s.GenerateDocumentAsync(user.Id, "SSM", It.IsAny<string>())).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(user.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("sign-token");

            var result = await controller.GenerateDocument(new GenerateDocumentDto { UserId = user.Id, DocumentType = "SSM" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GenerateDocument_ServiceThrows_ReturnsBadRequest()
        {
            var controller = CreateController();
            var user = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);
            _documentServiceMock.Setup(s => s.GenerateDocumentAsync(user.Id, "SSM", It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("already exists"));

            var result = await controller.GenerateDocument(new GenerateDocumentDto { UserId = user.Id, DocumentType = "SSM" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // ───────────────────────── GetUserDocuments / GetAllDocuments ─────────────────────────

        [Fact]
        public async Task GetUserDocuments_ReturnsMappedDocuments()
        {
            var controller = CreateController();
            var user = MakeUser();
            var doc = MakeDocument(user: user);
            _documentServiceMock.Setup(s => s.GetUserDocumentsAsync(user.Id)).ReturnsAsync(new[] { doc });

            var result = await controller.GetUserDocuments(user.Id);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        [Fact]
        public async Task GetAllDocuments_NonAdmin_FiltersToOwnDocuments()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.BasicUser);
            var myDoc = MakeDocument(user: MakeUser(id: callerId));
            var otherDoc = MakeDocument();
            _documentServiceMock.Setup(s => s.GetAllDocumentsAsync()).ReturnsAsync(new[] { myDoc, otherDoc });

            var result = await controller.GetAllDocuments();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        // ───────────────────────── Pending / Signed lists ─────────────────────────

        [Fact]
        public async Task GetMyPendingSignatures_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.GetMyPendingSignatures();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetMyPendingSignatures_ReturnsOnlyPendingUserStatus()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var pending = MakeDocument(user: MakeUser(id: callerId), status: "PendingUser");
            var completed = MakeDocument(user: MakeUser(id: callerId), status: "Completed");
            _documentServiceMock.Setup(s => s.GetUserDocumentsAsync(callerId)).ReturnsAsync(new[] { pending, completed });

            var result = await controller.GetMyPendingSignatures();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        [Fact]
        public async Task GetManagerPendingSignatures_ReturnsDocsFromService()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId);
            var awaiting = MakeDocument(status: "PendingManager");
            _documentServiceMock.Setup(s => s.GetManagerPendingSignaturesAsync(managerId)).ReturnsAsync(new[] { awaiting });

            var result = await controller.GetManagerPendingSignatures();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        [Fact]
        public async Task GetMySignedDocuments_ReturnsOnlySignedByUser()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var signed = MakeDocument(user: MakeUser(id: callerId));
            signed.UserSignedAt = DateTime.UtcNow;
            var unsigned = MakeDocument(user: MakeUser(id: callerId));
            _documentServiceMock.Setup(s => s.GetUserDocumentsAsync(callerId)).ReturnsAsync(new[] { signed, unsigned });

            var result = await controller.GetMySignedDocuments();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        [Fact]
        public async Task GetManagerSignedDocuments_ReturnsDocsFromService()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId);
            var signed = MakeDocument();
            signed.ManagerSignedAt = DateTime.UtcNow;
            _documentServiceMock.Setup(s => s.GetManagerSignedDocumentsAsync(managerId)).ReturnsAsync(new[] { signed });

            var result = await controller.GetManagerSignedDocuments();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        [Fact]
        public async Task GetAdminPendingSignatures_ReturnsMappedDocuments()
        {
            var controller = CreateController(role: Roles.Admin);
            _documentServiceMock.Setup(s => s.GetAdminPendingDocumentsAsync()).ReturnsAsync(new List<UserDocument> { MakeDocument() });

            var result = await controller.GetAdminPendingSignatures();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        [Fact]
        public async Task GetAdminSignedDocuments_ReturnsMappedDocuments()
        {
            var controller = CreateController(role: Roles.Admin);
            _documentServiceMock.Setup(s => s.GetAdminSignedDocumentsAsync()).ReturnsAsync(new List<UserDocument> { MakeDocument() });

            var result = await controller.GetAdminSignedDocuments();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Single(items);
        }

        // ───────────────────────── RegenerateDocuments ─────────────────────────

        [Fact]
        public async Task RegenerateDocuments_ReturnsCount()
        {
            var controller = CreateController(role: Roles.Admin);
            _documentServiceMock.Setup(s => s.RegenerateDocumentsAsync()).ReturnsAsync(5);

            var result = await controller.RegenerateDocuments();

            var ok = Assert.IsType<OkObjectResult>(result);
            var regenerated = (int)ok.Value!.GetType().GetProperty("regenerated")!.GetValue(ok.Value)!;
            Assert.Equal(5, regenerated);
        }

        // ───────────────────────── GetSignTokenForDocument ─────────────────────────

        [Fact]
        public async Task GetSignTokenForDocument_DocumentNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(It.IsAny<Guid>())).ReturnsAsync((UserDocument?)null);

            var result = await controller.GetSignTokenForDocument(Guid.NewGuid());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetSignTokenForDocument_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            var document = MakeDocument();
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.GetSignTokenForDocument(document.Id);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetSignTokenForDocument_ServiceForbids_ReturnsForbidden()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.BasicUser);
            var document = MakeDocument();
            var caller = MakeUser(id: callerId);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(callerId)).ReturnsAsync(caller);
            _documentSigningServiceMock.Setup(s => s.RequestSigningTokenAsync(document, caller, false))
                .ReturnsAsync(new SigningTokenResult { Forbidden = true });

            var result = await controller.GetSignTokenForDocument(document.Id);

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetSignTokenForDocument_ServiceReportsFailure_ReturnsBadRequest()
        {
            var owner = MakeUser();
            var controller = CreateController(owner.Id, role: Roles.BasicUser);
            var document = MakeDocument(user: owner);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(owner.Id)).ReturnsAsync(owner);
            _documentSigningServiceMock.Setup(s => s.RequestSigningTokenAsync(document, owner, false))
                .ReturnsAsync(new SigningTokenResult { ErrorMessage = "User already signed this document." });

            var result = await controller.GetSignTokenForDocument(document.Id);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("already signed", badRequest.Value!.ToString());
        }

        [Fact]
        public async Task GetSignTokenForDocument_ServiceSucceeds_ReturnsToken()
        {
            var owner = MakeUser();
            var controller = CreateController(owner.Id, role: Roles.BasicUser);
            var document = MakeDocument(user: owner, status: "PendingUser");
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(owner.Id)).ReturnsAsync(owner);
            _documentSigningServiceMock.Setup(s => s.RequestSigningTokenAsync(document, owner, false))
                .ReturnsAsync(new SigningTokenResult { Success = true, Token = "token-123" });

            var result = await controller.GetSignTokenForDocument(document.Id);

            var ok = Assert.IsType<OkObjectResult>(result);
            var token = (string)ok.Value!.GetType().GetProperty("token")!.GetValue(ok.Value)!;
            Assert.Equal("token-123", token);
        }

        // ───────────────────────── ViewPdf ─────────────────────────

        [Fact]
        public async Task ViewPdf_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.ViewPdf(Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task ViewPdf_DocumentNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(It.IsAny<Guid>())).ReturnsAsync((UserDocument?)null);

            var result = await controller.ViewPdf(Guid.NewGuid());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task ViewPdf_UnrelatedNonAdmin_ReturnsForbidden()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.BasicUser);
            var document = MakeDocument();
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);

            var result = await controller.ViewPdf(document.Id);

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task ViewPdf_Owner_ReturnsPdfFile()
        {
            var owner = MakeUser();
            var controller = CreateController(owner.Id, role: Roles.BasicUser);
            var document = MakeDocument(user: owner);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GeneratePdfBytesAsync(owner, document, false)).ReturnsAsync(new byte[] { 1, 2, 3 });

            var result = await controller.ViewPdf(document.Id);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/pdf", file.ContentType);
            Assert.Equal(3, file.FileContents.Length);
        }

        [Fact]
        public async Task ViewPdf_Manager_ReturnsPdfFile()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: Roles.LineManager);
            var owner = MakeUser(assignedToId: managerId);
            var document = MakeDocument(user: owner);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GeneratePdfBytesAsync(owner, document, false)).ReturnsAsync(new byte[] { 1, 2 });

            var result = await controller.ViewPdf(document.Id);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal(2, file.FileContents.Length);
        }

        [Fact]
        public async Task ViewPdf_Admin_ReturnsPdfFileWithViewerIsAdminTrue()
        {
            var controller = CreateController(role: Roles.Admin);
            var owner = MakeUser(); // unrelated to admin caller
            var document = MakeDocument(user: owner);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GeneratePdfBytesAsync(owner, document, true)).ReturnsAsync(new byte[] { 1, 2, 3, 4 });

            var result = await controller.ViewPdf(document.Id);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal(4, file.FileContents.Length);
        }

        // ───────────────────────── Additional BulkGenerateDocuments edge cases ─────────────────────────

        [Fact]
        public async Task BulkGenerateDocuments_SendsEmailOnlyToUnsignedUsersWithEmail()
        {
            var controller = CreateController(role: Roles.Admin);
            var needsEmail = MakeDocument(user: MakeUser());
            var alreadySigned = MakeDocument(user: MakeUser());
            alreadySigned.UserSignedAt = DateTime.UtcNow;

            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>())).ReturnsAsync((2, 0));
            _documentServiceMock.Setup(s => s.GetAllPendingUserDocumentsAsync("SSM")).ReturnsAsync(new[] { needsEmail, alreadySigned });
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(needsEmail.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(needsEmail.User!.Email, needsEmail.Id, It.IsAny<string>(), null))
                .ReturnsAsync("tok");

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "SSM" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(needsEmail.User!.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(alreadySigned.User!.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task BulkGenerateDocuments_EmailFailureForOneUser_DoesNotStopProcessingOthers()
        {
            var controller = CreateController(role: Roles.Admin);
            var failingDoc = MakeDocument(user: MakeUser());
            var succeedingDoc = MakeDocument(user: MakeUser());

            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>())).ReturnsAsync((2, 0));
            _documentServiceMock.Setup(s => s.GetAllPendingUserDocumentsAsync("SSM")).ReturnsAsync(new[] { failingDoc, succeedingDoc });
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(It.IsAny<Guid>())).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(failingDoc.User!.Email, failingDoc.Id, It.IsAny<string>(), null))
                .ThrowsAsync(new InvalidOperationException("token generation failed"));
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(succeedingDoc.User!.Email, succeedingDoc.Id, It.IsAny<string>(), null))
                .ReturnsAsync("tok");

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "SSM" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(succeedingDoc.User!.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(failingDoc.User!.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ───────────────────────── Additional GetAllDocuments edge case ─────────────────────────

        [Fact]
        public async Task GetAllDocuments_Admin_ReturnsAllDocumentsUnfiltered()
        {
            var controller = CreateController(role: Roles.Admin);
            _documentServiceMock.Setup(s => s.GetAllDocumentsAsync()).ReturnsAsync(new[] { MakeDocument(), MakeDocument(), MakeDocument() });

            var result = await controller.GetAllDocuments();

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>().ToList();
            Assert.Equal(3, items.Count);
        }

        // ───────────────────────── Additional GenerateDocument edge cases ─────────────────────────

        [Fact]
        public async Task GenerateDocument_LineManagerOfUser_Success()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: Roles.LineManager);
            var owner = MakeUser(assignedToId: managerId);
            var document = MakeDocument(user: owner);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(owner.Id)).ReturnsAsync(owner);
            _documentServiceMock.Setup(s => s.GenerateDocumentAsync(owner.Id, "SSM", It.IsAny<string>())).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(document.Id)).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(owner.Email, document.Id, It.IsAny<string>(), null))
                .ReturnsAsync("tok");

            var result = await controller.GenerateDocument(new GenerateDocumentDto { UserId = owner.Id, DocumentType = "SSM" });

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GenerateDocument_EmptyUserEmail_SkipsEmailSending()
        {
            var controller = CreateController(role: Roles.Admin);
            var owner = MakeUser();
            owner.Email = "";
            var document = MakeDocument(user: owner);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(owner.Id)).ReturnsAsync(owner);
            _documentServiceMock.Setup(s => s.GenerateDocumentAsync(owner.Id, "SSM", It.IsAny<string>())).ReturnsAsync(document);
            _documentServiceMock.Setup(s => s.GetDocumentByIdAsync(document.Id)).ReturnsAsync(document);

            var result = await controller.GenerateDocument(new GenerateDocumentDto { UserId = owner.Id, DocumentType = "SSM" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
