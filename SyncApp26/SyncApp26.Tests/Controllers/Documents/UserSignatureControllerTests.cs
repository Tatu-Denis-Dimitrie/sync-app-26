using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Shared.DTOs.Request.UserSignature;
using SyncApp26.Shared.DTOs.Response.UserSignature;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Documents
{
    public class UserSignatureControllerTests
    {
        private readonly Mock<IUserSignatureService> _signatureServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();

        private UserSignatureController CreateController(Guid? callerId = null, string role = "Basic User")
        {
            var controller = new UserSignatureController(_signatureServiceMock.Object, _userServiceMock.Object);
            controller.SetUser(callerId ?? Guid.NewGuid(), role: role);
            return controller;
        }

        private static UserSignature MakeSignature(Guid userId) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SignatureData = "base64data",
            SignatureMethod = "Draw",
            SignatureHash = "hash123",
            CreatedAt = DateTime.UtcNow
        };

        private static User MakeUser(Guid? id = null, Guid? assignedToId = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Roe",
            Email = $"jane.roe.{Guid.NewGuid():N}@example.com",
            PersonalId = Guid.NewGuid().ToString(),
            AssignedToId = assignedToId,
            RoleId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        // ───────────────────────── GetMySignature ─────────────────────────

        [Fact]
        public async Task GetMySignature_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.GetMySignature();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetMySignature_NoneExists_ReturnsOkWithNull()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            _signatureServiceMock.Setup(s => s.GetUserSignatureAsync(callerId)).ReturnsAsync((UserSignature?)null);

            var result = await controller.GetMySignature();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Null(ok.Value);
        }

        [Fact]
        public async Task GetMySignature_Exists_ReturnsDto()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var sig = MakeSignature(callerId);
            _signatureServiceMock.Setup(s => s.GetUserSignatureAsync(callerId)).ReturnsAsync(sig);

            var result = await controller.GetMySignature();

            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<UserSignatureResponseDTO>(ok.Value);
            Assert.Equal(sig.Id, dto.Id);
            Assert.True(dto.IsActive);
        }

        // ───────────────────────── GetUserSignature ─────────────────────────

        [Fact]
        public async Task GetUserSignature_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.GetUserSignature(Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetUserSignature_UnrelatedNonAdmin_ReturnsForbidden()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: "Basic User");

            var result = await controller.GetUserSignature(Guid.NewGuid());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetUserSignature_LineManagerOfTarget_ReturnsOk()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: "Line Manager");
            var target = MakeUser(assignedToId: managerId);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(target.Id)).ReturnsAsync(target);
            _signatureServiceMock.Setup(s => s.GetUserSignatureAsync(target.Id)).ReturnsAsync(MakeSignature(target.Id));

            var result = await controller.GetUserSignature(target.Id);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetUserSignature_Admin_NotFound_ReturnsNotFound()
        {
            var controller = CreateController(role: "Admin");
            var targetId = Guid.NewGuid();
            _signatureServiceMock.Setup(s => s.GetUserSignatureAsync(targetId)).ReturnsAsync((UserSignature?)null);

            var result = await controller.GetUserSignature(targetId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        // ───────────────────────── SaveMySignature ─────────────────────────

        [Fact]
        public async Task SaveMySignature_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.SaveMySignature(new SaveUserSignatureRequestDTO { SignatureData = "d", SignatureMethod = "Draw" });

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task SaveMySignature_MissingData_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.SaveMySignature(new SaveUserSignatureRequestDTO { SignatureData = "", SignatureMethod = "Draw" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveMySignature_InvalidMethod_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.SaveMySignature(new SaveUserSignatureRequestDTO { SignatureData = "d", SignatureMethod = "Stamp" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveMySignature_ServiceThrowsArgumentException_ReturnsBadRequest()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            _signatureServiceMock.Setup(s => s.SaveUserSignatureAsync(
                callerId, "d", "Draw", It.IsAny<string>(), callerId, It.IsAny<string>()))
                .ThrowsAsync(new ArgumentException("bad data"));

            var result = await controller.SaveMySignature(new SaveUserSignatureRequestDTO { SignatureData = "d", SignatureMethod = "Draw" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveMySignature_Success_ReturnsOkWithSavedSignature()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var savedSig = MakeSignature(callerId);
            _signatureServiceMock.Setup(s => s.SaveUserSignatureAsync(
                callerId, "d", "Draw", It.IsAny<string>(), callerId, It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            _signatureServiceMock.Setup(s => s.GetUserSignatureAsync(callerId)).ReturnsAsync(savedSig);

            var result = await controller.SaveMySignature(new SaveUserSignatureRequestDTO { SignatureData = "d", SignatureMethod = "Draw" });

            var ok = Assert.IsType<OkObjectResult>(result);
            var message = (string)ok.Value!.GetType().GetProperty("message")!.GetValue(ok.Value)!;
            Assert.Contains("saved successfully", message);
        }

        // ───────────────────────── RevokeMySignature ─────────────────────────

        [Fact]
        public async Task RevokeMySignature_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.RevokeMySignature();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task RevokeMySignature_NoActiveSignature_ReturnsNotFound()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            _signatureServiceMock.Setup(s => s.RevokeUserSignatureAsync(callerId, It.IsAny<string>(), callerId, It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("no signature"));

            var result = await controller.RevokeMySignature();

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task RevokeMySignature_Success_ReturnsOk()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            _signatureServiceMock.Setup(s => s.RevokeUserSignatureAsync(callerId, It.IsAny<string>(), callerId, It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var result = await controller.RevokeMySignature();

            Assert.IsType<OkObjectResult>(result);
        }

        // ───────────────────────── GetSignatureHistory ─────────────────────────

        [Fact]
        public async Task GetSignatureHistory_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.GetSignatureHistory(Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetSignatureHistory_UnrelatedNonAdmin_ReturnsForbidden()
        {
            var controller = CreateController(role: "Basic User");

            var result = await controller.GetSignatureHistory(Guid.NewGuid());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetSignatureHistory_Self_ReturnsOkWithHistory()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var history = new[]
            {
                new UserSignatureHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = callerId,
                    SignatureData = "d",
                    SignatureMethod = "Draw",
                    SignatureHash = "hash",
                    Action = "Created",
                    PerformedByUserId = callerId,
                    PerformedByEmail = "a@b.com"
                }
            };
            _signatureServiceMock.Setup(s => s.GetUserSignatureHistoryAsync(callerId)).ReturnsAsync(history);

            var result = await controller.GetSignatureHistory(callerId);

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<UserSignatureHistoryResponseDTO>>(ok.Value);
            Assert.Single(list);
        }

        // ───────────────────────── GetMySignatureHistory ─────────────────────────

        [Fact]
        public async Task GetMySignatureHistory_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.GetMySignatureHistory();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetMySignatureHistory_ReturnsOkWithHistory()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            _signatureServiceMock.Setup(s => s.GetUserSignatureHistoryAsync(callerId)).ReturnsAsync(Array.Empty<UserSignatureHistory>());

            var result = await controller.GetMySignatureHistory();

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<UserSignatureHistoryResponseDTO>>(ok.Value);
            Assert.Empty(list);
        }

        // ───────────────────────── Additional GetUserSignature edge cases ─────────────────────────

        [Fact]
        public async Task GetUserSignature_Admin_ReturnsOk()
        {
            var controller = CreateController(role: "Admin");
            var targetId = Guid.NewGuid();
            _signatureServiceMock.Setup(s => s.GetUserSignatureAsync(targetId)).ReturnsAsync(MakeSignature(targetId));

            var result = await controller.GetUserSignature(targetId);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetUserSignature_LineManagerNotManagingTarget_ReturnsForbidden()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: "Line Manager");
            var target = MakeUser(); // not assigned to this manager
            _userServiceMock.Setup(s => s.GetUserByIdAsync(target.Id)).ReturnsAsync(target);

            var result = await controller.GetUserSignature(target.Id);

            Assert.IsType<ForbidResult>(result);
        }

        // ───────────────────────── Additional GetSignatureHistory edge cases ─────────────────────────

        [Fact]
        public async Task GetSignatureHistory_Admin_ReturnsOk()
        {
            var controller = CreateController(role: "Admin");
            var targetId = Guid.NewGuid();
            _signatureServiceMock.Setup(s => s.GetUserSignatureHistoryAsync(targetId)).ReturnsAsync(Array.Empty<UserSignatureHistory>());

            var result = await controller.GetSignatureHistory(targetId);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetSignatureHistory_LineManagerOfTarget_ReturnsOk()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController(managerId, role: "Line Manager");
            var target = MakeUser(assignedToId: managerId);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(target.Id)).ReturnsAsync(target);
            _signatureServiceMock.Setup(s => s.GetUserSignatureHistoryAsync(target.Id)).ReturnsAsync(Array.Empty<UserSignatureHistory>());

            var result = await controller.GetSignatureHistory(target.Id);

            Assert.IsType<OkObjectResult>(result);
        }

        // ───────────────────────── Additional SaveMySignature edge case ─────────────────────────

        [Fact]
        public async Task SaveMySignature_TypeMethod_Success()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);
            var savedSig = MakeSignature(callerId);
            savedSig.SignatureMethod = "Type";
            _signatureServiceMock.Setup(s => s.SaveUserSignatureAsync(
                callerId, "typed-signature", "Type", It.IsAny<string>(), callerId, It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            _signatureServiceMock.Setup(s => s.GetUserSignatureAsync(callerId)).ReturnsAsync(savedSig);

            var result = await controller.SaveMySignature(new SaveUserSignatureRequestDTO { SignatureData = "typed-signature", SignatureMethod = "Type" });

            Assert.IsType<OkObjectResult>(result);
            _signatureServiceMock.Verify(s => s.SaveUserSignatureAsync(
                callerId, "typed-signature", "Type", It.IsAny<string>(), callerId, It.IsAny<string>()), Times.Once);
        }
    }
}
