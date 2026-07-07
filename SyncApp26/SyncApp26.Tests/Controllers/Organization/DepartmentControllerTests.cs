using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.Department;
using SyncApp26.Shared.DTOs.Response.Department;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Organization
{
    public class DepartmentControllerTests
    {
        private readonly Mock<IDepartmentService> _departmentServiceMock = new();
        private readonly Mock<IUserService> _userServiceMock = new();

        private DepartmentController CreateController(string role = Roles.Admin)
        {
            var controller = new DepartmentController(_departmentServiceMock.Object);
            controller.SetUser(Guid.NewGuid(), role: role);
            return controller;
        }

        private static Department MakeDepartment(Guid? id = null, bool isActive = true) => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Engineering",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        // ───────────────────────── GetDepartmentById ─────────────────────────

        [Fact]
        public async Task GetDepartmentById_Found_ReturnsOk()
        {
            var controller = CreateController();
            var department = MakeDepartment();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(department.Id)).ReturnsAsync(department);

            var result = await controller.GetDepartmentById(department.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<DepartmentGETResponseDTO>(ok.Value);
            Assert.Equal(department.Id, dto.Id);
            Assert.Equal(department.Name, dto.Name);
        }

        [Fact]
        public async Task GetDepartmentById_Missing_ReturnsNotFound()
        {
            var controller = CreateController();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Department?)null);

            var result = await controller.GetDepartmentById(Guid.NewGuid());

            Assert.IsType<NotFoundResult>(result.Result);
        }

        // ───────────────────────── GetAllDepartments ─────────────────────────

        [Fact]
        public async Task GetAllDepartments_ReturnsMappedList()
        {
            var controller = CreateController();
            _departmentServiceMock.Setup(s => s.GetAllDepartmentsAsync()).ReturnsAsync(new[] { MakeDepartment(), MakeDepartment() });

            var result = await controller.GetAllDepartments();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<DepartmentGETResponseDTO>>(ok.Value);
            Assert.Equal(2, list.Count());
        }

        // ───────────────────────── GetScheduledForDeletionDepartments ─────────────────────────

        [Fact]
        public async Task GetScheduledForDeletionDepartments_ReturnsDeletedAtPopulated()
        {
            var controller = CreateController();
            var deleted = MakeDepartment(isActive: false);
            deleted.DeletedAt = DateTime.UtcNow;
            _departmentServiceMock.Setup(s => s.GetDeletedDepartmentsAsync()).ReturnsAsync(new[] { deleted });

            var result = await controller.GetScheduledForDeletionDepartments();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<DepartmentGETResponseDTO>>(ok.Value).ToList();
            Assert.Single(list);
            Assert.NotNull(list[0].DeletedAt);
        }

        // ───────────────────────── RestoreDepartment ─────────────────────────

        [Fact]
        public async Task RestoreDepartment_NotFound_ReturnsFailureDto()
        {
            var controller = CreateController();
            _departmentServiceMock.Setup(s => s.GetDeletedDepartmentByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Department?)null);

            var result = await controller.RestoreDepartment(Guid.NewGuid());

            Assert.False(result.Value!.Success);
        }

        [Fact]
        public async Task RestoreDepartment_Found_RestoresAsInactive()
        {
            var controller = CreateController();
            var department = MakeDepartment(isActive: false);
            department.DeletedAt = DateTime.UtcNow;
            _departmentServiceMock.Setup(s => s.GetDeletedDepartmentByIdAsync(department.Id)).ReturnsAsync(department);

            var result = await controller.RestoreDepartment(department.Id);

            Assert.True(result.Value!.Success);
            Assert.Null(department.DeletedAt);
            Assert.False(department.IsActive);
            _departmentServiceMock.Verify(s => s.UpdateDepartmentAsync(department), Times.Once);
        }

        // ───────────────────────── AddDepartment ─────────────────────────

        [Fact]
        public async Task AddDepartment_Success_TrimsNameAndAdds()
        {
            var controller = CreateController();
            var request = new DepartmentRequestDTO { Name = "  Sales  ", IsActive = true };

            var result = await controller.AddDepartment(request);

            Assert.True(result.Value!.Success);
            _departmentServiceMock.Verify(s => s.AddDepartmentAsync(It.Is<Department>(d => d.Name == "Sales" && d.IsActive)), Times.Once);
        }

        [Fact]
        public async Task AddDepartment_EmptyOrWhitespaceName_IsAcceptedWithoutValidation()
        {
            // Documents current behavior: unlike UpdateDepartment, AddDepartment performs no
            // name validation, so a blank name is silently accepted and persisted as an empty string
            var controller = CreateController();

            var result = await controller.AddDepartment(new DepartmentRequestDTO { Name = "   ", IsActive = true });

            Assert.True(result.Value!.Success);
            _departmentServiceMock.Verify(s => s.AddDepartmentAsync(It.Is<Department>(d => d.Name == "")), Times.Once);
        }

        // ───────────────────────── UpdateDepartment ─────────────────────────

        [Fact]
        public async Task UpdateDepartment_MissingName_ReturnsFailureDto()
        {
            var controller = CreateController();

            var result = await controller.UpdateDepartment(Guid.NewGuid(), new DepartmentRequestDTO { Name = "" });

            Assert.False(result.Value!.Success);
            _departmentServiceMock.Verify(s => s.UpdateDepartmentAsync(It.IsAny<Department>()), Times.Never);
        }

        [Fact]
        public async Task UpdateDepartment_Success_TrimsNameAndUpdates()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();

            var result = await controller.UpdateDepartment(id, new DepartmentRequestDTO { Name = "  HR  ", IsActive = false });

            Assert.True(result.Value!.Success);
            _departmentServiceMock.Verify(s => s.UpdateDepartmentAsync(It.Is<Department>(d => d.Id == id && d.Name == "HR" && !d.IsActive)), Times.Once);
        }

        // ───────────────────────── DeleteDepartment ─────────────────────────

        [Fact]
        public async Task DeleteDepartment_NotFound_ReturnsFailureDto()
        {
            var controller = CreateController();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Department?)null);

            var result = await controller.DeleteDepartment(Guid.NewGuid(), null, _userServiceMock.Object);

            Assert.False(result.Value!.Success);
        }

        [Fact]
        public async Task DeleteDepartment_NoUsersAssigned_SoftDeletesDepartment()
        {
            var controller = CreateController();
            var department = MakeDepartment();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(department.Id)).ReturnsAsync(department);
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(department.Id)).ReturnsAsync(Array.Empty<User>());

            var result = await controller.DeleteDepartment(department.Id, null, _userServiceMock.Object);

            Assert.True(result.Value!.Success);
            Assert.False(department.IsActive);
            Assert.NotNull(department.DeletedAt);
            _departmentServiceMock.Verify(s => s.UpdateDepartmentAsync(department), Times.Once);
        }

        [Fact]
        public async Task DeleteDepartment_UsersAssignedNoTransferId_ReturnsBadRequest()
        {
            var controller = CreateController();
            var department = MakeDepartment();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(department.Id)).ReturnsAsync(department);
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(department.Id))
                .ReturnsAsync(new[] { new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, CreatedAt = DateTime.UtcNow } });

            var result = await controller.DeleteDepartment(department.Id, null, _userServiceMock.Object);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<DepartmentResponseDTO>(badRequest.Value);
            Assert.Contains("transfer department ID", dto.Message);
        }

        [Fact]
        public async Task DeleteDepartment_TransferDepartmentNotFound_ReturnsBadRequest()
        {
            var controller = CreateController();
            var department = MakeDepartment();
            var transferId = Guid.NewGuid();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(department.Id)).ReturnsAsync(department);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(transferId)).ReturnsAsync((Department?)null);
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(department.Id))
                .ReturnsAsync(new[] { new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, CreatedAt = DateTime.UtcNow } });

            var result = await controller.DeleteDepartment(department.Id, transferId, _userServiceMock.Object);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<DepartmentResponseDTO>(badRequest.Value);
            Assert.Contains("Transfer department not found", dto.Message);
        }

        [Fact]
        public async Task DeleteDepartment_TransferToSameDepartment_ReturnsBadRequest()
        {
            var controller = CreateController();
            var department = MakeDepartment();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(department.Id)).ReturnsAsync(department);
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(department.Id))
                .ReturnsAsync(new[] { new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, CreatedAt = DateTime.UtcNow } });

            var result = await controller.DeleteDepartment(department.Id, department.Id, _userServiceMock.Object);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<DepartmentResponseDTO>(badRequest.Value);
            Assert.Contains("same department", dto.Message);
        }

        [Fact]
        public async Task DeleteDepartment_WithTransfer_MovesUsersAndSoftDeletes()
        {
            var controller = CreateController();
            var department = MakeDepartment();
            var transferDepartment = MakeDepartment();
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, DepartmentId = department.Id, CreatedAt = DateTime.UtcNow };

            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(department.Id)).ReturnsAsync(department);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(transferDepartment.Id)).ReturnsAsync(transferDepartment);
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(department.Id)).ReturnsAsync(new[] { user });

            var result = await controller.DeleteDepartment(department.Id, transferDepartment.Id, _userServiceMock.Object);

            Assert.True(result.Value!.Success);
            Assert.Equal(transferDepartment.Id, user.DepartmentId);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
            _departmentServiceMock.Verify(s => s.UpdateDepartmentAsync(department), Times.Once);
        }
    }
}
