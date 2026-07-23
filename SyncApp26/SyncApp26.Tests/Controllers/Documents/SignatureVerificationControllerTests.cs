using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.SignatureVerification;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Documents
{
    public class SignatureVerificationControllerTests
    {
        private readonly Mock<ISignatureVerificationService> _verificationServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();

        private SignatureVerificationController CreateController(Guid? callerId = null, string role = Roles.BasicUser)
        {
            var controller = new SignatureVerificationController(_verificationServiceMock.Object, _userServiceMock.Object);
            controller.SetUser(callerId ?? Guid.NewGuid(), role: role);
            return controller;
        }

        private static SignatureVerificationStatusResponseDTO MakeStatus(Guid signatureId, Guid signerUserId, string status = "Valid") => new()
        {
            SignatureId = signatureId,
            SignerUserId = signerUserId,
            Status = status,
            IsHashValid = status == "Valid",
            IsChainValid = status == "Valid",
            IsLegacy = status == "Legacy",
            VerifiedAt = DateTimeOffset.UtcNow
        };

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

        // ───────────────────────── GetVerificationStatus ─────────────────────────

        [Fact]
        public async Task GetVerificationStatus_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.GetVerificationStatus(Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatus_UnknownId_ReturnsNotFound()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusAsync(id)).ReturnsAsync((SignatureVerificationStatusResponseDTO?)null);

            var result = await controller.GetVerificationStatus(id);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatus_Self_ReturnsOk()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var id = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusAsync(id)).ReturnsAsync(MakeStatus(id, callerId));

            var result = await controller.GetVerificationStatus(id);

            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<SignatureVerificationStatusResponseDTO>(ok.Value);
            Assert.Equal("Valid", dto.Status);
        }

        [Fact]
        public async Task GetVerificationStatus_UnrelatedNonAdmin_ReturnsForbidden()
        {
            var controller = CreateController(role: Roles.BasicUser);
            var id = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusAsync(id)).ReturnsAsync(MakeStatus(id, Guid.NewGuid()));

            var result = await controller.GetVerificationStatus(id);

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatus_Admin_ReturnsOk()
        {
            var controller = CreateController(role: Roles.Admin);
            var id = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusAsync(id)).ReturnsAsync(MakeStatus(id, Guid.NewGuid()));

            var result = await controller.GetVerificationStatus(id);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatus_LineManagerOfSigner_ReturnsOk()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: Roles.LineManager);
            var signer = MakeUser(assignedToId: managerId);
            var id = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusAsync(id)).ReturnsAsync(MakeStatus(id, signer.Id));
            _userServiceMock.Setup(s => s.GetUserByIdAsync(signer.Id)).ReturnsAsync(signer);

            var result = await controller.GetVerificationStatus(id);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatus_LineManagerNotManagingSigner_ReturnsForbidden()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: Roles.LineManager);
            var signer = MakeUser(); // not assigned to this manager
            var id = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusAsync(id)).ReturnsAsync(MakeStatus(id, signer.Id));
            _userServiceMock.Setup(s => s.GetUserByIdAsync(signer.Id)).ReturnsAsync(signer);

            var result = await controller.GetVerificationStatus(id);

            Assert.IsType<ForbidResult>(result);
        }

        // ───────────────────────── GetVerificationStatusBatch ─────────────────────────

        [Fact]
        public async Task GetVerificationStatusBatch_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.GetVerificationStatusBatch(new BatchVerificationStatusRequestDTO { SignatureIds = new List<Guid> { Guid.NewGuid() } });

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatusBatch_EmptyList_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.GetVerificationStatusBatch(new BatchVerificationStatusRequestDTO { SignatureIds = new List<Guid>() });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatusBatch_TooManyIds_ReturnsBadRequest()
        {
            var controller = CreateController();
            var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();

            var result = await controller.GetVerificationStatusBatch(new BatchVerificationStatusRequestDTO { SignatureIds = ids });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetVerificationStatusBatch_FiltersOutResultsCallerCannotAccess()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.BasicUser);
            var ownId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var otherSignerId = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusBatchAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new List<SignatureVerificationStatusResponseDTO>
                {
                    MakeStatus(ownId, callerId),
                    MakeStatus(otherId, otherSignerId)
                });

            var result = await controller.GetVerificationStatusBatch(new BatchVerificationStatusRequestDTO { SignatureIds = new List<Guid> { ownId, otherId } });

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
            Assert.Single(list);
        }

        [Fact]
        public async Task GetVerificationStatusBatch_IncludesNotFoundEntriesRegardlessOfCaller()
        {
            var controller = CreateController(role: Roles.BasicUser);
            var unknownId = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusBatchAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new List<SignatureVerificationStatusResponseDTO>
                {
                    MakeStatus(unknownId, Guid.Empty, "NotFound")
                });

            var result = await controller.GetVerificationStatusBatch(new BatchVerificationStatusRequestDTO { SignatureIds = new List<Guid> { unknownId } });

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
            Assert.Single(list);
        }

        [Fact]
        public async Task GetVerificationStatusBatch_Admin_ReturnsAllResults()
        {
            var controller = CreateController(role: Roles.Admin);
            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();
            _verificationServiceMock.Setup(s => s.GetVerificationStatusBatchAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new List<SignatureVerificationStatusResponseDTO>
                {
                    MakeStatus(idA, Guid.NewGuid()),
                    MakeStatus(idB, Guid.NewGuid())
                });

            var result = await controller.GetVerificationStatusBatch(new BatchVerificationStatusRequestDTO { SignatureIds = new List<Guid> { idA, idB } });

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
            Assert.Equal(2, list.Count());
        }
    }
}
