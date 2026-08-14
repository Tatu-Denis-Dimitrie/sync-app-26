using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.WorkSite;
using SyncApp26.Shared.DTOs.Response.WorkSite;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Organization
{
    public class WorkSiteControllerTests
    {
        private readonly Mock<IWorkSiteService> _workSiteServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();

        private WorkSiteController CreateController(string role = Roles.Admin)
        {
            var controller = new WorkSiteController(_workSiteServiceMock.Object);
            controller.SetUser(Guid.NewGuid(), role: role);
            return controller;
        }

        private static WorkSite MakeWorkSite(Guid? id = null, bool isActive = true) => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Main Plant",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        // ───────────────────────── GetWorkSiteById ─────────────────────────

        [Fact]
        public async Task GetWorkSiteById_Found_ReturnsOk()
        {
            var controller = CreateController();
            var workSite = MakeWorkSite();
            _workSiteServiceMock.Setup(s => s.GetWorkSiteByIdAsync(workSite.Id)).ReturnsAsync(workSite);

            var result = await controller.GetWorkSiteById(workSite.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<WorkSiteGETResponseDTO>(ok.Value);
            Assert.Equal(workSite.Id, dto.Id);
            Assert.Equal(workSite.Name, dto.Name);
        }

        [Fact]
        public async Task GetWorkSiteById_Missing_ReturnsNotFound()
        {
            var controller = CreateController();
            _workSiteServiceMock.Setup(s => s.GetWorkSiteByIdAsync(It.IsAny<Guid>())).ReturnsAsync((WorkSite?)null);

            var result = await controller.GetWorkSiteById(Guid.NewGuid());

            Assert.IsType<NotFoundResult>(result.Result);
        }

        // ───────────────────────── GetAllWorkSites ─────────────────────────

        [Fact]
        public async Task GetAllWorkSites_ReturnsMappedList()
        {
            var controller = CreateController();
            _workSiteServiceMock.Setup(s => s.GetAllWorkSitesAsync()).ReturnsAsync(new[] { MakeWorkSite(), MakeWorkSite() });

            var result = await controller.GetAllWorkSites();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<WorkSiteGETResponseDTO>>(ok.Value);
            Assert.Equal(2, list.Count());
        }

        // ───────────────────────── GetScheduledForDeletionWorkSites ─────────────────────────

        [Fact]
        public async Task GetScheduledForDeletionWorkSites_ReturnsDeletedAtPopulated()
        {
            var controller = CreateController();
            var deleted = MakeWorkSite(isActive: false);
            deleted.DeletedAt = DateTime.UtcNow;
            _workSiteServiceMock.Setup(s => s.GetDeletedWorkSitesAsync()).ReturnsAsync(new[] { deleted });

            var result = await controller.GetScheduledForDeletionWorkSites();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<WorkSiteGETResponseDTO>>(ok.Value).ToList();
            Assert.Single(list);
            Assert.NotNull(list[0].DeletedAt);
        }

        // ───────────────────────── RestoreWorkSite ─────────────────────────

        [Fact]
        public async Task RestoreWorkSite_NotFound_ReturnsFailureDto()
        {
            var controller = CreateController();
            _workSiteServiceMock.Setup(s => s.GetDeletedWorkSiteByIdAsync(It.IsAny<Guid>())).ReturnsAsync((WorkSite?)null);

            var result = await controller.RestoreWorkSite(Guid.NewGuid());

            Assert.False(result.Value!.Success);
        }

        [Fact]
        public async Task RestoreWorkSite_Found_RestoresAsInactive()
        {
            var controller = CreateController();
            var workSite = MakeWorkSite(isActive: false);
            workSite.DeletedAt = DateTime.UtcNow;
            _workSiteServiceMock.Setup(s => s.GetDeletedWorkSiteByIdAsync(workSite.Id)).ReturnsAsync(workSite);

            var result = await controller.RestoreWorkSite(workSite.Id);

            Assert.True(result.Value!.Success);
            Assert.Null(workSite.DeletedAt);
            Assert.False(workSite.IsActive);
            _workSiteServiceMock.Verify(s => s.UpdateWorkSiteAsync(workSite), Times.Once);
        }

        // ───────────────────────── AddWorkSite ─────────────────────────

        [Fact]
        public async Task AddWorkSite_Success_TrimsNameAndAdds()
        {
            var controller = CreateController();
            var request = new WorkSiteRequestDTO { Name = "  Main Plant  ", IsActive = true };

            var result = await controller.AddWorkSite(request);

            Assert.True(result.Value!.Success);
            _workSiteServiceMock.Verify(s => s.AddWorkSiteAsync(It.Is<WorkSite>(w => w.Name == "Main Plant" && w.IsActive)), Times.Once);
        }

        // ───────────────────────── UpdateWorkSite ─────────────────────────

        [Fact]
        public async Task UpdateWorkSite_MissingName_ReturnsFailureDto()
        {
            var controller = CreateController();

            var result = await controller.UpdateWorkSite(Guid.NewGuid(), new WorkSiteRequestDTO { Name = "" });

            Assert.False(result.Value!.Success);
            _workSiteServiceMock.Verify(s => s.UpdateWorkSiteAsync(It.IsAny<WorkSite>()), Times.Never);
        }

        [Fact]
        public async Task UpdateWorkSite_Success_TrimsNameAndUpdates()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();

            var result = await controller.UpdateWorkSite(id, new WorkSiteRequestDTO { Name = "  Depot  ", IsActive = false });

            Assert.True(result.Value!.Success);
            _workSiteServiceMock.Verify(s => s.UpdateWorkSiteAsync(It.Is<WorkSite>(w => w.Id == id && w.Name == "Depot" && !w.IsActive)), Times.Once);
        }

        // ───────────────────────── DeleteWorkSite ─────────────────────────

        [Fact]
        public async Task DeleteWorkSite_NotFound_ReturnsFailureDto()
        {
            var controller = CreateController();
            _workSiteServiceMock.Setup(s => s.GetWorkSiteByIdAsync(It.IsAny<Guid>())).ReturnsAsync((WorkSite?)null);

            var result = await controller.DeleteWorkSite(Guid.NewGuid(), _userServiceMock.Object);

            Assert.False(result.Value!.Success);
        }

        [Fact]
        public async Task DeleteWorkSite_NoUsersAssigned_SoftDeletesWorkSite()
        {
            var controller = CreateController();
            var workSite = MakeWorkSite();
            _workSiteServiceMock.Setup(s => s.GetWorkSiteByIdAsync(workSite.Id)).ReturnsAsync(workSite);
            _userServiceMock.Setup(s => s.GetUsersByWorkSiteIdAsync(workSite.Id)).ReturnsAsync(Array.Empty<User>());

            var result = await controller.DeleteWorkSite(workSite.Id, _userServiceMock.Object);

            Assert.True(result.Value!.Success);
            Assert.False(workSite.IsActive);
            Assert.NotNull(workSite.DeletedAt);
            _workSiteServiceMock.Verify(s => s.UpdateWorkSiteAsync(workSite), Times.Once);
        }

        [Fact]
        public async Task DeleteWorkSite_UsersAssigned_UnassignsThenSoftDeletes()
        {
            var controller = CreateController();
            var workSite = MakeWorkSite();
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", WorkSiteId = workSite.Id, CreatedAt = DateTime.UtcNow };

            _workSiteServiceMock.Setup(s => s.GetWorkSiteByIdAsync(workSite.Id)).ReturnsAsync(workSite);
            _userServiceMock.Setup(s => s.GetUsersByWorkSiteIdAsync(workSite.Id)).ReturnsAsync(new[] { user });

            var result = await controller.DeleteWorkSite(workSite.Id, _userServiceMock.Object);

            Assert.True(result.Value!.Success);
            Assert.Null(user.WorkSiteId);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
            Assert.False(workSite.IsActive);
            _workSiteServiceMock.Verify(s => s.UpdateWorkSiteAsync(workSite), Times.Once);
        }
    }
}
