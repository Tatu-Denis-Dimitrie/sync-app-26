using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.DataChange;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Requests
{
    public class DataChangeRequestControllerTests
    {
        private readonly Mock<IDataChangeRequestService> _serviceMock = new();
        private readonly Mock<IEmailService> _emailServiceMock = new();
        private readonly Mock<IDataChangeRequestRepository> _repositoryMock = new();

        private DataChangeRequestController CreateController(Guid? callerId = null)
        {
            var controller = new DataChangeRequestController(
                _serviceMock.Object,
                _emailServiceMock.Object,
                _repositoryMock.Object,
                NullLogger<DataChangeRequestController>.Instance);

            controller.SetUser(callerId ?? Guid.NewGuid(), role: Roles.Admin);
            return controller;
        }

        private static User MakeUser(Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Roe",
            Email = $"jane.roe.{Guid.NewGuid():N}@example.com",
            PersonalId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow
        };

        private static DataChangeRequestDTO MakeResolvedDto(Guid userId, string status) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserEmail = "jane.roe@example.com",
            UserFullName = "Jane Roe",
            RequestedChangesJson = "{}",
            Reason = "Address update",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            ResolvedAt = DateTime.UtcNow
        };

        // ───────────────────────── Create: allowlist enforcement ─────────────────────────
        // Generalizes the endpoint's old Email/Role-only stripping into a real allowlist check
        // against the service's own AllowedFields, matching the mass-assignment fix there.

        private static readonly string[] TestAllowedFields = { "FirstName", "Address" };

        [Fact]
        public async Task Create_DisallowedFieldOnly_ReturnsBadRequestNamingIt()
        {
            var controller = CreateController();
            _serviceMock.SetupGet(s => s.AllowedFields).Returns(TestAllowedFields);
            var dto = new CreateDataChangeRequestDTO { RequestedChangesJson = "{\"PasswordHash\":\"x\"}", Reason = "Attempted" };

            var result = await controller.Create(dto);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("PasswordHash", GetProp<string>(badRequest.Value!, "message"));
            _serviceMock.Verify(s => s.CreateRequestAsync(It.IsAny<Guid>(), It.IsAny<CreateDataChangeRequestDTO>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task Create_NonStringValueInJson_FailsClosedInsteadOfSkippingAllowlistCheck()
        {
            // Regression: a non-string value used to throw, hit an empty catch{}, and skip filtering.
            var controller = CreateController();
            _serviceMock.SetupGet(s => s.AllowedFields).Returns(TestAllowedFields);
            var dto = new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"Address\":\"x\",\"Email\":\"attacker@evil.com\",\"IsActive\":false}",
                Reason = "Attempted"
            };

            var result = await controller.Create(dto);

            Assert.IsType<BadRequestObjectResult>(result);
            _serviceMock.Verify(s => s.CreateRequestAsync(It.IsAny<Guid>(), It.IsAny<CreateDataChangeRequestDTO>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task Create_MixOfAllowedAndDisallowedFields_StripsDisallowedAndStillSucceeds()
        {
            var controller = CreateController();
            _serviceMock.SetupGet(s => s.AllowedFields).Returns(TestAllowedFields);
            CreateDataChangeRequestDTO? forwarded = null;
            _serviceMock.Setup(s => s.CreateRequestAsync(It.IsAny<Guid>(), It.IsAny<CreateDataChangeRequestDTO>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<Guid, CreateDataChangeRequestDTO, string, bool>((_, d, _, _) => forwarded = d)
                .ReturnsAsync(new DataChangeRequestDTO { Id = Guid.NewGuid(), Status = "Pending" });
            var dto = new CreateDataChangeRequestDTO
            {
                RequestedChangesJson = "{\"FirstName\":\"New\",\"Role\":\"Admin\"}",
                Reason = "Name changed legally"
            };

            var result = await controller.Create(dto);

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(forwarded);
            Assert.DoesNotContain("Role", forwarded!.RequestedChangesJson);
            Assert.Contains("FirstName", forwarded.RequestedChangesJson);
        }

        [Fact]
        public async Task Create_AllowedFieldsOnly_PassesThroughUnchanged()
        {
            var controller = CreateController();
            _serviceMock.SetupGet(s => s.AllowedFields).Returns(TestAllowedFields);
            _serviceMock.Setup(s => s.CreateRequestAsync(It.IsAny<Guid>(), It.IsAny<CreateDataChangeRequestDTO>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new DataChangeRequestDTO { Id = Guid.NewGuid(), Status = "Pending" });
            var dto = new CreateDataChangeRequestDTO { RequestedChangesJson = "{\"FirstName\":\"New\"}", Reason = "Name changed legally" };

            var result = await controller.Create(dto);

            Assert.IsType<OkObjectResult>(result);
            _serviceMock.Verify(s => s.CreateRequestAsync(It.IsAny<Guid>(),
                It.Is<CreateDataChangeRequestDTO>(d => d.RequestedChangesJson.Contains("FirstName")),
                It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task Resolve_ApprovedAndEmailSucceeds_ReturnsOkWithNullEmailError()
        {
            var controller = CreateController();
            var user = MakeUser();
            var resolved = MakeResolvedDto(user.Id, "Approved");

            _serviceMock.Setup(s => s.ResolveRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ResolveDataChangeRequestDTO>()))
                .ReturnsAsync(resolved);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.Id)).ReturnsAsync(user);
            _emailServiceMock.Setup(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var result = await controller.Resolve(resolved.Id, new ResolveDataChangeRequestDTO { Status = "Approved" });

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Null(GetProp<string?>(okResult.Value!, "emailError"));
            Assert.Equal("Approved", GetProp<DataChangeRequestDTO>(okResult.Value!, "request").Status);
        }

        [Fact]
        public async Task Resolve_ApprovedAndEmailFails_StillReturnsOkWithEmailError()
        {
            var controller = CreateController();
            var user = MakeUser();
            var resolved = MakeResolvedDto(user.Id, "Approved");

            _serviceMock.Setup(s => s.ResolveRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ResolveDataChangeRequestDTO>()))
                .ReturnsAsync(resolved);
            _repositoryMock.Setup(r => r.GetUserByIdAsync(user.Id)).ReturnsAsync(user);
            _emailServiceMock.Setup(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("SMTP is not configured."));

            var result = await controller.Resolve(resolved.Id, new ResolveDataChangeRequestDTO { Status = "Approved" });

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("SMTP is not configured.", GetProp<string?>(okResult.Value!, "emailError"));
            Assert.Equal("Approved", GetProp<DataChangeRequestDTO>(okResult.Value!, "request").Status);
        }

        [Fact]
        public async Task Resolve_Rejected_NeverCallsEmailService()
        {
            var controller = CreateController();
            var user = MakeUser();
            var resolved = MakeResolvedDto(user.Id, "Rejected");

            _serviceMock.Setup(s => s.ResolveRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ResolveDataChangeRequestDTO>()))
                .ReturnsAsync(resolved);

            var result = await controller.Resolve(resolved.Id, new ResolveDataChangeRequestDTO { Status = "Rejected" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Resolve_ServiceThrows_ReturnsBadRequest()
        {
            var controller = CreateController();

            _serviceMock.Setup(s => s.ResolveRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ResolveDataChangeRequestDTO>()))
                .ThrowsAsync(new InvalidOperationException("Request not found"));

            var result = await controller.Resolve(Guid.NewGuid(), new ResolveDataChangeRequestDTO { Status = "Approved" });

            Assert.IsType<BadRequestObjectResult>(result);
            _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        private static T GetProp<T>(object obj, string name) => (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;
    }
}
