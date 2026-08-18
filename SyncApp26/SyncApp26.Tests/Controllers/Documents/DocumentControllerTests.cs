using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Response.Document;
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
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();

        private static T GetProp<T>(object obj, string name) => (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;

        // Points the background job's scope at the same mocks the test already set up, so a job
        // started by bulk-generate-async resolves the mocked services instead of real ones.
        private void UseMocksInBackgroundScope()
        {
            var providerMock = new Mock<IServiceProvider>();
            providerMock.Setup(p => p.GetService(typeof(IDocumentService))).Returns(_documentServiceMock.Object);
            providerMock.Setup(p => p.GetService(typeof(IDocumentSignatureService))).Returns(_documentSignatureServiceMock.Object);
            providerMock.Setup(p => p.GetService(typeof(IEmailService))).Returns(_emailServiceMock.Object);

            var scopeMock = new Mock<IServiceScope>();
            scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);
            _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        }

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
                _configurationMock.Object,
                _scopeFactoryMock.Object);

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
        public async Task BulkGenerateDocuments_Admin_ReturnsForbidden()
        {
            // Admin has no standing to initiate anything anymore — app administration and SSM/SU
            // responsibility are separate duties.
            var controller = CreateController(role: Roles.Admin);

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "Both" });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task BulkGenerateDocuments_SsmOfficerRequestingBoth_GeneratesOnlySsm()
        {
            // Holding the officer role for one type gives no standing on the other — SU is silently
            // dropped from the request rather than failing the whole call.
            var controller = CreateController(role: Roles.SsmOfficer);
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult { Generated = 2, Skipped = 0 });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new List<UserDocument>());

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "Both" });

            Assert.IsType<OkObjectResult>(result);
            _documentServiceMock.Verify(s => s.BulkGenerateDocumentsAsync("SSM", It.IsAny<string>(), null, null, It.IsAny<Action<int, int>?>()), Times.Once);
            _documentServiceMock.Verify(s => s.BulkGenerateDocumentsAsync("SU", It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()), Times.Never);
        }

        [Fact]
        public async Task BulkGenerateDocuments_NonAdmin_PassesCallerIdAsRestriction()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.LineManager);
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult { Generated = 1, Skipped = 0 });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<UserDocument>());

            var request = new BulkGenerateDocumentDto { DocumentType = "SSM" };
            var result = await controller.BulkGenerateDocuments(request);

            Assert.IsType<OkObjectResult>(result);
            _documentServiceMock.Verify(s => s.BulkGenerateDocumentsAsync("SSM", It.IsAny<string>(), request.SelectedUserIds, callerId, It.IsAny<Action<int, int>?>()), Times.Once);
        }

        [Fact]
        public async Task BulkGenerateDocuments_TwoTypes_MessageBreaksDownTheTotalPerType()
        {
            // "78 generated" for a Both run is really 39 SSM + 39 SU; without the breakdown it reads
            // as double-counting to whoever is looking at 39 new rows per type.
            // A line manager reaches both types through the direct-reports fallback, which is the
            // simplest way to get a two-type run without forging a dual-officer principal.
            var controller = CreateController(role: Roles.LineManager);
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult { Generated = 39, Skipped = 0 });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<UserDocument>());

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "Both" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(78, GetProp<int>(ok.Value!, "generated"));
            var message = GetProp<string>(ok.Value!, "message")!;
            Assert.Contains("78 document(s) generated", message);
            Assert.Contains("39 SSM", message);
            Assert.Contains("39 SU", message);
        }

        [Fact]
        public async Task BulkGenerateDocuments_EmailsFailingRepeatedly_StopsAfterThreeAndReportsIt()
        {
            // A broken SMTP server fails every message and each attempt still costs a connect
            // timeout, so the run must not keep paying that once per document.
            var controller = CreateController(role: Roles.SsmOfficer);
            var docs = Enumerable.Range(0, 20).Select(_ => MakeDocument(user: MakeUser())).ToList();

            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult { Generated = 20, Skipped = 0, GeneratedDocumentIds = docs.Select(d => d.Id).ToList() });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(docs);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(It.IsAny<Guid>())).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), null)).ReturnsAsync("tok");
            _emailServiceMock.Setup(s => s.SendDocumentSignatureEmailWithLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("SMTP settings are missing."));

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "SSM" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(0, GetProp<int>(ok.Value!, "emailsSent"));
            Assert.Equal(3, GetProp<int>(ok.Value!, "emailsFailed"));
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(3));
            // The failure must be reported, not silently reported as "0 sent".
            Assert.Contains("SMTP settings are missing.", GetProp<string>(ok.Value!, "message")!);
        }

        [Fact]
        public async Task BulkGenerateDocuments_OneBadRecipient_DoesNotAbortTheRest()
        {
            // The consecutive-failure cutoff must not trip on isolated per-recipient failures.
            var controller = CreateController(role: Roles.SsmOfficer);
            var docs = Enumerable.Range(0, 6).Select(_ => MakeDocument(user: MakeUser())).ToList();
            var badEmail = docs[0].User!.Email;

            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult { Generated = 6, Skipped = 0, GeneratedDocumentIds = docs.Select(d => d.Id).ToList() });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(docs);
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(It.IsAny<Guid>())).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), null)).ReturnsAsync("tok");
            _emailServiceMock.Setup(s => s.SendDocumentSignatureEmailWithLinkAsync(badEmail, It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("bad recipient"));

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "SSM" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(5, GetProp<int>(ok.Value!, "emailsSent"));
            Assert.Equal(1, GetProp<int>(ok.Value!, "emailsFailed"));
        }

        // ───────────────────────── BulkGenerateDocumentsAsync (job-based) ─────────────────────────

        [Fact]
        public async Task BulkGenerateDocumentsAsync_NoTargetUsers_ReturnsNullJobId()
        {
            var controller = CreateController(role: Roles.SsmOfficer);
            _documentServiceMock.Setup(s => s.GetBulkGenerateTargetUserIdsAsync(It.IsAny<List<Guid>?>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new List<Guid>());

            var result = await controller.BulkGenerateDocumentsAsync(new BulkGenerateDocumentDto { DocumentType = "SSM" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Null(GetProp<string?>(ok.Value!, "jobId"));
            Assert.Equal(0, GetProp<int>(ok.Value!, "total"));
        }

        [Fact]
        public async Task BulkGenerateDocumentsAsync_Admin_ReturnsForbidden()
        {
            var controller = CreateController(role: Roles.Admin);

            var result = await controller.BulkGenerateDocumentsAsync(new BulkGenerateDocumentDto { DocumentType = "Both" });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task BulkGenerateDocumentsAsync_StartsJobAndSizesTotalFromTargetUsers()
        {
            var controller = CreateController(role: Roles.SsmOfficer);
            UseMocksInBackgroundScope();
            _documentServiceMock.Setup(s => s.GetBulkGenerateTargetUserIdsAsync(It.IsAny<List<Guid>?>(), It.IsAny<Guid?>()))
                .ReturnsAsync(Enumerable.Range(0, 39).Select(_ => Guid.NewGuid()).ToList());
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult { Generated = 39, Skipped = 0 });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<UserDocument>());

            var result = await controller.BulkGenerateDocumentsAsync(new BulkGenerateDocumentDto { DocumentType = "SSM" });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False(string.IsNullOrEmpty(GetProp<string?>(ok.Value!, "jobId")));
            Assert.Equal(39, GetProp<int>(ok.Value!, "total"));
        }

        [Fact]
        public async Task GetBulkGenerateStatus_BackgroundJobThatCannotStart_StillCompletesWithAnError()
        {
            // A job that faults before reaching its finally would stay Completed=false forever and
            // the client would poll it indefinitely, so failures must still terminate the job.
            var controller = CreateController(role: Roles.SsmOfficer);
            var emptyProvider = new Mock<IServiceProvider>(); // resolves nothing
            var scopeMock = new Mock<IServiceScope>();
            scopeMock.Setup(s => s.ServiceProvider).Returns(emptyProvider.Object);
            _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
            _documentServiceMock.Setup(s => s.GetBulkGenerateTargetUserIdsAsync(It.IsAny<List<Guid>?>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new List<Guid> { Guid.NewGuid() });

            var started = Assert.IsType<OkObjectResult>(await controller.BulkGenerateDocumentsAsync(new BulkGenerateDocumentDto { DocumentType = "SSM" }));
            var jobId = GetProp<string>(started.Value!, "jobId")!;

            var deadline = DateTime.UtcNow.AddSeconds(5);
            OkObjectResult status;
            do
            {
                status = Assert.IsType<OkObjectResult>(controller.GetBulkGenerateStatus(jobId));
                if (GetProp<bool>(status.Value!, "completed")) break;
                await Task.Delay(20);
            } while (DateTime.UtcNow < deadline);

            Assert.True(GetProp<bool>(status.Value!, "completed"));
            Assert.NotNull(GetProp<string?>(status.Value!, "error"));
        }

        [Fact]
        public void GetBulkGenerateStatus_UnknownJob_ReturnsNotFound()
        {
            var controller = CreateController(role: Roles.SsmOfficer);

            var result = controller.GetBulkGenerateStatus(Guid.NewGuid().ToString());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetBulkGenerateStatus_JobOwnedByAnotherUser_ReturnsForbidden()
        {
            var owner = CreateController(role: Roles.SsmOfficer);
            UseMocksInBackgroundScope();
            _documentServiceMock.Setup(s => s.GetBulkGenerateTargetUserIdsAsync(It.IsAny<List<Guid>?>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new List<Guid> { Guid.NewGuid() });
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult { Generated = 1, Skipped = 0 });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<UserDocument>());

            var started = Assert.IsType<OkObjectResult>(await owner.BulkGenerateDocumentsAsync(new BulkGenerateDocumentDto { DocumentType = "SSM" }));
            var jobId = GetProp<string>(started.Value!, "jobId")!;

            var stranger = CreateController(Guid.NewGuid(), role: Roles.SsmOfficer);
            Assert.IsType<ForbidResult>(stranger.GetBulkGenerateStatus(jobId));
        }

        [Fact]
        public async Task GetBulkGenerateStatus_ReportsProgressAndCompletesWithoutArtificialDelay()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.SsmOfficer);
            UseMocksInBackgroundScope();
            _documentServiceMock.Setup(s => s.GetBulkGenerateTargetUserIdsAsync(It.IsAny<List<Guid>?>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() });
            // Drive the progress callback the way the real service does, one tick per document.
            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync((string _, string _, List<Guid>? _, Guid? _, Action<int, int>? onProgress) =>
                {
                    for (int i = 1; i <= 3; i++) onProgress?.Invoke(i, 0);
                    return new BulkGenerateResult { Generated = 3, Skipped = 0 };
                });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<UserDocument>());

            var started = Assert.IsType<OkObjectResult>(await controller.BulkGenerateDocumentsAsync(new BulkGenerateDocumentDto { DocumentType = "SSM" }));
            var jobId = GetProp<string>(started.Value!, "jobId")!;

            // No Task.Delay pacing in this job, so it settles almost immediately.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            OkObjectResult status;
            do
            {
                status = Assert.IsType<OkObjectResult>(controller.GetBulkGenerateStatus(jobId));
                if (GetProp<bool>(status.Value!, "completed")) break;
                await Task.Delay(20);
            } while (DateTime.UtcNow < deadline);

            Assert.True(GetProp<bool>(status.Value!, "completed"));
            Assert.Equal(3, GetProp<int>(status.Value!, "total"));
            Assert.Equal(3, GetProp<int>(status.Value!, "generated"));
            Assert.Equal("done", GetProp<string>(status.Value!, "phase"));
            Assert.Null(GetProp<string?>(status.Value!, "error"));
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
        public async Task GenerateDocument_Admin_ReturnsForbidden()
        {
            // Admin has no standing to initiate anything anymore, even for an unrelated employee.
            var controller = CreateController(role: Roles.Admin);
            var user = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await controller.GenerateDocument(new GenerateDocumentDto { UserId = user.Id, DocumentType = "SSM" });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GenerateDocument_Success_SendsSignatureEmail()
        {
            var controller = CreateController(role: Roles.SsmOfficer);
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
            var controller = CreateController(role: Roles.SsmOfficer);
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
            _documentServiceMock.Setup(s => s.GetUserDocumentsPageAsync(user.Id, 1, 10)).ReturnsAsync((new List<UserDocument> { doc }, 1));

            var result = await controller.GetUserDocuments(user.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<DocumentListPageDTO>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
        }

        [Fact]
        public async Task GetUserDocuments_AsOwner_ReturnsOk()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.BasicUser);
            var doc = MakeDocument(user: MakeUser(id: callerId));
            _documentServiceMock.Setup(s => s.GetUserDocumentsPageAsync(callerId, 1, 10)).ReturnsAsync((new List<UserDocument> { doc }, 1));

            var result = await controller.GetUserDocuments(callerId);

            Assert.IsType<OkObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetUserDocuments_AsOwnLineManager_ReturnsOk()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: Roles.LineManager);
            var report = MakeUser(assignedToId: managerId);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(report.Id)).ReturnsAsync(report);
            _documentServiceMock.Setup(s => s.GetUserDocumentsPageAsync(report.Id, 1, 10)).ReturnsAsync((new List<UserDocument> { MakeDocument(user: report) }, 1));

            var result = await controller.GetUserDocuments(report.Id);

            Assert.IsType<OkObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetUserDocuments_AsUnrelatedBasicUser_ReturnsForbid()
        {
            var controller = CreateController(Guid.NewGuid(), role: Roles.BasicUser);
            var target = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(target.Id)).ReturnsAsync(target);

            var result = await controller.GetUserDocuments(target.Id);

            Assert.IsType<ForbidResult>(result.Result);
            _documentServiceMock.Verify(s => s.GetUserDocumentsPageAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
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

            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public async Task GetMyPendingSignatures_ReturnsOnlyPendingUserStatus()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var pending = MakeDocument(user: MakeUser(id: callerId), status: "PendingUser");
            _documentServiceMock.Setup(s => s.GetMyPendingSignaturesPageAsync(callerId, 1, 10))
                .ReturnsAsync((new List<UserDocument> { pending }, 1));

            var result = await controller.GetMyPendingSignatures();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<DocumentListPageDTO>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
        }

        [Fact]
        public async Task GetMyPendingSignatures_ClampsPageAndPageSize()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            _documentServiceMock.Setup(s => s.GetMyPendingSignaturesPageAsync(callerId, 1, 100))
                .ReturnsAsync((new List<UserDocument>(), 0));

            var result = await controller.GetMyPendingSignatures(page: 0, pageSize: 500);

            Assert.IsType<OkObjectResult>(result.Result);
            _documentServiceMock.Verify(s => s.GetMyPendingSignaturesPageAsync(callerId, 1, 100), Times.Once);
        }

        [Fact]
        public async Task GetManagerPendingSignatures_ReturnsDocsFromService()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId);
            var awaiting = MakeDocument(status: "PendingManager");
            _documentServiceMock.Setup(s => s.GetManagerPendingSignaturesAsync(managerId, 1, 10))
                .ReturnsAsync((new List<UserDocument> { awaiting }, 1));

            var result = await controller.GetManagerPendingSignatures();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<DocumentListPageDTO>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
        }

        [Fact]
        public async Task GetMySignedDocuments_ReturnsOnlySignedByUser()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var signed = MakeDocument(user: MakeUser(id: callerId));
            signed.UserSignedAt = DateTime.UtcNow;
            _documentServiceMock.Setup(s => s.GetMySignedDocumentsPageAsync(callerId, 1, 10))
                .ReturnsAsync((new List<UserDocument> { signed }, 1));

            var result = await controller.GetMySignedDocuments();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<DocumentListPageDTO>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
        }

        [Fact]
        public async Task GetManagerSignedDocuments_ReturnsDocsFromService()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId);
            var signed = MakeDocument();
            signed.ManagerSignedAt = DateTime.UtcNow;
            _documentServiceMock.Setup(s => s.GetManagerSignedDocumentsAsync(managerId, 1, 10))
                .ReturnsAsync((new List<UserDocument> { signed }, 1));

            var result = await controller.GetManagerSignedDocuments();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<DocumentListPageDTO>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
        }

        [Fact]
        public async Task GetInstructorPendingSignatures_ReturnsDocsFromService()
        {
            var instructorId = Guid.NewGuid();
            var controller = CreateController(instructorId);
            var awaiting = MakeDocument(status: "PendingInstructor");
            _documentServiceMock.Setup(s => s.GetInstructorPendingSignaturesAsync(instructorId, 1, 10))
                .ReturnsAsync((new List<UserDocument> { awaiting }, 1));

            var result = await controller.GetInstructorPendingSignatures();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<DocumentListPageDTO>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
        }

        [Fact]
        public async Task GetInstructorSignedDocuments_ReturnsDocsFromService()
        {
            var instructorId = Guid.NewGuid();
            var controller = CreateController(instructorId);
            var signed = MakeDocument();
            signed.InstructorSignedAt = DateTime.UtcNow;
            _documentServiceMock.Setup(s => s.GetInstructorSignedDocumentsAsync(instructorId, 1, 10))
                .ReturnsAsync((new List<UserDocument> { signed }, 1));

            var result = await controller.GetInstructorSignedDocuments();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<DocumentListPageDTO>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal(1, page.TotalCount);
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
            _documentSigningServiceMock.Setup(s => s.RequestSigningTokenAsync(document, caller))
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
            _documentSigningServiceMock.Setup(s => s.RequestSigningTokenAsync(document, owner))
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
            _documentSigningServiceMock.Setup(s => s.RequestSigningTokenAsync(document, owner))
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
            var controller = CreateController(role: Roles.SsmOfficer);
            var needsEmail = MakeDocument(user: MakeUser());
            var alreadySigned = MakeDocument(user: MakeUser());
            alreadySigned.UserSignedAt = DateTime.UtcNow;

            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult
                {
                    Generated = 2,
                    Skipped = 0,
                    GeneratedDocumentIds = new List<Guid> { needsEmail.Id, alreadySigned.Id }
                });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<UserDocument> { needsEmail, alreadySigned });
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
            var controller = CreateController(role: Roles.SsmOfficer);
            var failingDoc = MakeDocument(user: MakeUser());
            var succeedingDoc = MakeDocument(user: MakeUser());

            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult
                {
                    Generated = 2,
                    Skipped = 0,
                    GeneratedDocumentIds = new List<Guid> { failingDoc.Id, succeedingDoc.Id }
                });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync(new List<UserDocument> { failingDoc, succeedingDoc });
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

        [Fact]
        public async Task BulkGenerateDocuments_EmailsOnlyTheDocumentsThisRunGenerated()
        {
            // Regression: this used to email every PendingUser document in the database, so the mail
            // volume grew with document history instead of with the run — an employee with 8 stale
            // unsigned documents received 8 emails per bulk generation, and a 39-document run sent
            // 312 sequential SMTP messages. Only the ids this run produced may be notified.
            var controller = CreateController(role: Roles.SsmOfficer);
            var freshlyGenerated = MakeDocument(user: MakeUser());
            var staleBacklogDoc = MakeDocument(user: MakeUser());

            _documentServiceMock.Setup(s => s.BulkGenerateDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<Action<int, int>?>()))
                .ReturnsAsync(new BulkGenerateResult
                {
                    Generated = 1,
                    Skipped = 0,
                    GeneratedDocumentIds = new List<Guid> { freshlyGenerated.Id }
                });
            _documentServiceMock.Setup(s => s.GetPendingUserDocumentsByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IEnumerable<Guid> ids) =>
                    new[] { freshlyGenerated, staleBacklogDoc }.Where(d => ids.Contains(d.Id)).ToList());
            _documentServiceMock.Setup(s => s.GetCurrentTrainingIdForDocumentAsync(It.IsAny<Guid>())).ReturnsAsync((Guid?)null);
            _documentSignatureServiceMock.Setup(s => s.GenerateSignatureTokenAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), null))
                .ReturnsAsync("tok");

            var result = await controller.BulkGenerateDocuments(new BulkGenerateDocumentDto { DocumentType = "SSM" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(freshlyGenerated.User!.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _emailServiceMock.Verify(s => s.SendDocumentSignatureEmailWithLinkAsync(staleBacklogDoc.User!.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            // The whole-backlog query must not be reachable from this path at all.
            _documentServiceMock.Verify(s => s.GetAllPendingUserDocumentsAsync(It.IsAny<string>()), Times.Never);
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

        [Fact]
        public async Task GetAllDocuments_SsmOfficer_ReturnsAllDocumentsUnfiltered()
        {
            // An officer's duty spans every employee, not just their own reports — same breadth as admin.
            var controller = CreateController(role: Roles.SsmOfficer);
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
            var controller = CreateController(role: Roles.SsmOfficer);
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
