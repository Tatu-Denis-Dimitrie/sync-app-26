using Moq;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.Request.User;

namespace SyncApp26.Tests.Services.Auth
{
    public class RoleServiceTests
    {
        private readonly Mock<IUserRepository> _userRepositoryMock = new();

        private RoleService CreateService() => new(_userRepositoryMock.Object);

        // ───────────────────────── GetAllRolesAsync ─────────────────────────

        [Fact]
        public async Task GetAllRolesAsync_ReturnsMappedRoles()
        {
            var role = new Role { Id = Guid.NewGuid(), Name = "SsmOfficer", Description = "desc", IsSystem = true, CreatedAt = DateTime.UtcNow };
            _userRepositoryMock.Setup(r => r.GetAllRolesAsync()).ReturnsAsync(new List<Role> { role });
            var service = CreateService();

            var result = await service.GetAllRolesAsync();

            var dto = Assert.Single(result);
            Assert.Equal(role.Id, dto.Id);
            Assert.Equal("SsmOfficer", dto.Name);
            Assert.True(dto.IsSystem);
        }

        // ───────────────────────── CreateRoleAsync ─────────────────────────

        [Fact]
        public async Task CreateRoleAsync_EmptyName_Throws()
        {
            var service = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRoleAsync(new CreateRoleRequestDTO { Name = "  " }));
        }

        [Fact]
        public async Task CreateRoleAsync_DuplicateName_Throws()
        {
            _userRepositoryMock.Setup(r => r.GetRoleByNameAsync("Auditor")).ReturnsAsync(new Role { Id = Guid.NewGuid(), Name = "Auditor" });
            var service = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRoleAsync(new CreateRoleRequestDTO { Name = "Auditor" }));
        }

        [Fact]
        public async Task CreateRoleAsync_ValidName_CreatesNonSystemRole()
        {
            _userRepositoryMock.Setup(r => r.GetRoleByNameAsync("Auditor")).ReturnsAsync((Role?)null);
            var service = CreateService();

            var result = await service.CreateRoleAsync(new CreateRoleRequestDTO { Name = " Auditor ", Description = "Reviews records" });

            Assert.Equal("Auditor", result.Name);
            Assert.Equal("Reviews records", result.Description);
            Assert.False(result.IsSystem);
            _userRepositoryMock.Verify(r => r.AddRoleAsync(It.Is<Role>(role => role.Name == "Auditor" && !role.IsSystem)), Times.Once);
        }

        // ───────────────────────── DeleteRoleAsync ─────────────────────────

        [Fact]
        public async Task DeleteRoleAsync_NotFound_Throws()
        {
            _userRepositoryMock.Setup(r => r.GetRoleByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Role?)null);
            var service = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteRoleAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task DeleteRoleAsync_SystemRole_Throws()
        {
            var role = new Role { Id = Guid.NewGuid(), Name = "Admin", IsSystem = true };
            _userRepositoryMock.Setup(r => r.GetRoleByIdAsync(role.Id)).ReturnsAsync(role);
            var service = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteRoleAsync(role.Id));
            _userRepositoryMock.Verify(r => r.DeleteRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task DeleteRoleAsync_HasAssignments_Throws()
        {
            var role = new Role { Id = Guid.NewGuid(), Name = "Auditor", IsSystem = false };
            _userRepositoryMock.Setup(r => r.GetRoleByIdAsync(role.Id)).ReturnsAsync(role);
            _userRepositoryMock.Setup(r => r.RoleHasAssignmentsAsync(role.Id)).ReturnsAsync(true);
            var service = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteRoleAsync(role.Id));
            _userRepositoryMock.Verify(r => r.DeleteRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task DeleteRoleAsync_UnassignedCustomRole_Deletes()
        {
            var role = new Role { Id = Guid.NewGuid(), Name = "Auditor", IsSystem = false };
            _userRepositoryMock.Setup(r => r.GetRoleByIdAsync(role.Id)).ReturnsAsync(role);
            _userRepositoryMock.Setup(r => r.RoleHasAssignmentsAsync(role.Id)).ReturnsAsync(false);
            var service = CreateService();

            await service.DeleteRoleAsync(role.Id);

            _userRepositoryMock.Verify(r => r.DeleteRoleAsync(role), Times.Once);
        }
    }
}
