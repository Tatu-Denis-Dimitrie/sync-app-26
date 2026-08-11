using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Auth
{
    public class ImpersonationControllerTests
    {
        private readonly Mock<IImpersonationService> _impersonationServiceMock = new();

        private ImpersonationController CreateController(Guid? callerId = null, string role = Roles.Admin)
        {
            var controller = new ImpersonationController(_impersonationServiceMock.Object);
            controller.SetUser(callerId ?? Guid.NewGuid(), role: role);
            return controller;
        }

        private static T GetProp<T>(object obj, string name) => (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;

        [Fact]
        public async Task Impersonate_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.Impersonate(Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Impersonate_Success_ReturnsOkWithTokenAndUser()
        {
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var controller = CreateController(adminId);
            _impersonationServiceMock.Setup(s => s.StartAsync(adminId, targetId, It.IsAny<string?>()))
                .ReturnsAsync(new ImpersonationResult
                {
                    Status = ImpersonationStatus.Success,
                    Token = "fake-token",
                    UserId = targetId,
                    Email = "target@test.com",
                    FirstName = "Target",
                    LastName = "User",
                    Roles = new[] { Roles.BasicUser }
                });

            var result = await controller.Impersonate(targetId);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("fake-token", GetProp<string?>(ok.Value!, "token"));
            var user = GetProp<object>(ok.Value!, "user");
            Assert.Equal(targetId, GetProp<Guid>(user, "id"));
        }

        [Fact]
        public async Task Impersonate_TargetNotFound_ReturnsNotFound()
        {
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var controller = CreateController(adminId);
            _impersonationServiceMock.Setup(s => s.StartAsync(adminId, targetId, It.IsAny<string?>()))
                .ReturnsAsync(new ImpersonationResult { Status = ImpersonationStatus.TargetNotFound });

            var result = await controller.Impersonate(targetId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task Impersonate_TargetIsAdmin_ReturnsForbidden403()
        {
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var controller = CreateController(adminId);
            _impersonationServiceMock.Setup(s => s.StartAsync(adminId, targetId, It.IsAny<string?>()))
                .ReturnsAsync(new ImpersonationResult { Status = ImpersonationStatus.TargetIsAdmin });

            var result = await controller.Impersonate(targetId);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, objectResult.StatusCode);
        }

        [Fact]
        public async Task Impersonate_SelfImpersonation_ReturnsBadRequest()
        {
            var adminId = Guid.NewGuid();
            var controller = CreateController(adminId);
            _impersonationServiceMock.Setup(s => s.StartAsync(adminId, adminId, It.IsAny<string?>()))
                .ReturnsAsync(new ImpersonationResult { Status = ImpersonationStatus.SelfImpersonation });

            var result = await controller.Impersonate(adminId);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Impersonate_PassesCallerIdFromClaimAsImpersonator()
        {
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var controller = CreateController(adminId);
            _impersonationServiceMock.Setup(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()))
                .ReturnsAsync(new ImpersonationResult { Status = ImpersonationStatus.TargetNotFound });

            await controller.Impersonate(targetId);

            _impersonationServiceMock.Verify(s => s.StartAsync(adminId, targetId, It.IsAny<string?>()), Times.Once);
        }
    }
}
