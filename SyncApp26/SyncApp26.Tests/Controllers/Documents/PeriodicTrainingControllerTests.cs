using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.PeriodicTraining;
using SyncApp26.Shared.DTOs.Response.PeriodicTraining;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Documents
{
    public class PeriodicTrainingControllerTests
    {
        private readonly Mock<IPeriodicTrainingService> _periodicTrainingServiceMock = new();

        private PeriodicTrainingController CreateController(Guid? callerId = null, string role = Roles.Admin)
        {
            var controller = new PeriodicTrainingController(_periodicTrainingServiceMock.Object);
            controller.SetUser(callerId ?? Guid.NewGuid(), role: role);
            return controller;
        }

        // ───────────────────────── Create ─────────────────────────

        [Fact]
        public async Task Create_Success_ReturnsOk()
        {
            var controller = CreateController();
            var dto = new CreatePeriodicTrainingDTO { UserId = Guid.NewGuid() };
            var response = new PeriodicTrainingResponseDTO { Id = Guid.NewGuid(), UserId = dto.UserId };
            _periodicTrainingServiceMock.Setup(s => s.CreateAsync(dto)).ReturnsAsync(response);

            var result = await controller.Create(dto);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(response, ok.Value);
        }

        [Fact]
        public async Task Create_ServiceThrows_ReturnsBadRequest()
        {
            var controller = CreateController();
            var dto = new CreatePeriodicTrainingDTO { UserId = Guid.NewGuid() };
            _periodicTrainingServiceMock.Setup(s => s.CreateAsync(dto)).ThrowsAsync(new InvalidOperationException("bad user"));

            var result = await controller.Create(dto);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // ───────────────────────── GetById ─────────────────────────

        [Fact]
        public async Task GetById_NotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _periodicTrainingServiceMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((PeriodicTrainingResponseDTO?)null);

            var result = await controller.GetById(Guid.NewGuid());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetById_Found_ReturnsOk()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();
            var response = new PeriodicTrainingResponseDTO { Id = id };
            _periodicTrainingServiceMock.Setup(s => s.GetByIdAsync(id)).ReturnsAsync(response);

            var result = await controller.GetById(id);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(response, ok.Value);
        }

        // ───────────────────────── GetByUserId ─────────────────────────

        [Fact]
        public async Task GetByUserId_ReturnsOkWithList()
        {
            var controller = CreateController();
            var userId = Guid.NewGuid();
            _periodicTrainingServiceMock.Setup(s => s.GetByUserIdAsync(userId))
                .ReturnsAsync(new[] { new PeriodicTrainingResponseDTO { Id = Guid.NewGuid(), UserId = userId } });

            var result = await controller.GetByUserId(userId);

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<PeriodicTrainingResponseDTO>>(ok.Value);
            Assert.Single(list);
        }

        // ───────────────────────── Update ─────────────────────────

        [Fact]
        public async Task Update_Success_ReturnsOk()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();
            var dto = new UpdatePeriodicTrainingDTO { Occupation = "Welder" };
            var response = new PeriodicTrainingResponseDTO { Id = id, Occupation = "Welder" };
            _periodicTrainingServiceMock.Setup(s => s.UpdateAsync(id, dto)).ReturnsAsync(response);

            var result = await controller.Update(id, dto);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(response, ok.Value);
        }

        [Fact]
        public async Task Update_NotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();
            var dto = new UpdatePeriodicTrainingDTO();
            _periodicTrainingServiceMock.Setup(s => s.UpdateAsync(id, dto)).ThrowsAsync(new ArgumentException("not found"));

            var result = await controller.Update(id, dto);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task Update_UnexpectedException_ReturnsBadRequest()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();
            var dto = new UpdatePeriodicTrainingDTO();
            _periodicTrainingServiceMock.Setup(s => s.UpdateAsync(id, dto)).ThrowsAsync(new InvalidOperationException("boom"));

            var result = await controller.Update(id, dto);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // ───────────────────────── Delete ─────────────────────────

        [Fact]
        public async Task Delete_NotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _periodicTrainingServiceMock.Setup(s => s.DeleteAsync(It.IsAny<Guid>())).ReturnsAsync(false);

            var result = await controller.Delete(Guid.NewGuid());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task Delete_Success_ReturnsOk()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();
            _periodicTrainingServiceMock.Setup(s => s.DeleteAsync(id)).ReturnsAsync(true);

            var result = await controller.Delete(id);

            Assert.IsType<OkObjectResult>(result);
        }

        // ───────────────────────── BulkCreate ─────────────────────────

        [Fact]
        public async Task BulkCreate_ServiceThrows_ReturnsBadRequest()
        {
            var controller = CreateController();
            var dto = new BulkCreatePeriodicTrainingDTO { ApplyToAllUsers = true };
            _periodicTrainingServiceMock.Setup(s => s.BulkCreateAsync(It.IsAny<BulkCreatePeriodicTrainingDTO>(), It.IsAny<Guid?>())).ThrowsAsync(new Exception("boom"));

            var result = await controller.BulkCreate(dto);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task BulkCreate_AllFailed_ReturnsBadRequest()
        {
            var controller = CreateController();
            var dto = new BulkCreatePeriodicTrainingDTO { ApplyToAllUsers = true };
            var resultDto = new BulkCreateResultDTO { SuccessCount = 0, FailedCount = 2, Errors = new List<string> { "err1", "err2" } };
            _periodicTrainingServiceMock.Setup(s => s.BulkCreateAsync(It.IsAny<BulkCreatePeriodicTrainingDTO>(), It.IsAny<Guid?>())).ReturnsAsync(resultDto);

            var result = await controller.BulkCreate(dto);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task BulkCreate_Admin_PassesNullRestriction()
        {
            var controller = CreateController(role: Roles.Admin);
            var dto = new BulkCreatePeriodicTrainingDTO { ApplyToAllUsers = true };
            var resultDto = new BulkCreateResultDTO { SuccessCount = 3 };
            _periodicTrainingServiceMock.Setup(s => s.BulkCreateAsync(dto, null)).ReturnsAsync(resultDto);

            var result = await controller.BulkCreate(dto);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(resultDto, ok.Value);
            _periodicTrainingServiceMock.Verify(s => s.BulkCreateAsync(dto, null), Times.Once);
        }

        [Fact]
        public async Task BulkCreate_NonAdmin_PassesCallerIdAsRestriction()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId, role: Roles.LineManager);
            var dto = new BulkCreatePeriodicTrainingDTO { ApplyToAllUsers = true };
            _periodicTrainingServiceMock.Setup(s => s.BulkCreateAsync(dto, callerId)).ReturnsAsync(new BulkCreateResultDTO { SuccessCount = 1 });

            var result = await controller.BulkCreate(dto);

            Assert.IsType<OkObjectResult>(result);
            _periodicTrainingServiceMock.Verify(s => s.BulkCreateAsync(dto, callerId), Times.Once);
        }

        [Fact]
        public async Task BulkCreate_PartialSuccess_StillReturnsOk()
        {
            var controller = CreateController();
            var dto = new BulkCreatePeriodicTrainingDTO { ApplyToAllUsers = true };
            var resultDto = new BulkCreateResultDTO { SuccessCount = 2, FailedCount = 1, Errors = new List<string> { "one failure" } };
            _periodicTrainingServiceMock.Setup(s => s.BulkCreateAsync(It.IsAny<BulkCreatePeriodicTrainingDTO>(), It.IsAny<Guid?>())).ReturnsAsync(resultDto);

            var result = await controller.BulkCreate(dto);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(resultDto, ok.Value);
        }
    }
}
