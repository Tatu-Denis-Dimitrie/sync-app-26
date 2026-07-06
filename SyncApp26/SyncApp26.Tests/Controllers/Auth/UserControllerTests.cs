using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.PeriodicTraining;
using SyncApp26.Shared.DTOs.Response.User;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Auth
{
    public class UserControllerTests : IDisposable
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<IDepartmentService> _departmentServiceMock = new();
        private readonly Mock<IFunctionService> _functionServiceMock = new();
        private readonly Mock<IUserChangeHistoryService> _userChangeHistoryServiceMock = new();
        private readonly Mock<IDocumentService> _documentServiceMock = new();
        private readonly Mock<IPeriodicTrainingService> _periodicTrainingServiceMock = new();
        private readonly SqliteContextFixture _dbFixture = new();

        public void Dispose() => _dbFixture.Dispose();

        private UserController CreateController()
        {
            var controller = new UserController(
                _userServiceMock.Object,
                _departmentServiceMock.Object,
                _functionServiceMock.Object,
                _userChangeHistoryServiceMock.Object,
                _documentServiceMock.Object,
                _periodicTrainingServiceMock.Object,
                _dbFixture.Context);

            controller.SetUser(Guid.NewGuid(), role: "Admin");
            return controller;
        }

        private void StubEmptyDocumentSets()
        {
            _documentServiceMock.Setup(s => s.GetUserIdsWithDocumentTypeAsync(It.IsAny<string>())).ReturnsAsync(new HashSet<Guid>());
            _documentServiceMock.Setup(s => s.GetUserIdsWithUnsignedDocumentTypeAsync(It.IsAny<string>())).ReturnsAsync(new HashSet<Guid>());
        }

        private static User MakeUser(Guid? id = null, Guid? departmentId = null, Guid? assignedToId = null)
        {
            var userId = id ?? Guid.NewGuid();
            return new User
            {
                Id = userId,
                FirstName = "John",
                LastName = "Doe",
                Email = $"john.doe.{userId:N}@example.com",
                PersonalId = Guid.NewGuid().ToString(),
                DepartmentId = departmentId,
                AssignedToId = assignedToId,
                RoleId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
        }

        private void SeedUserRow(User user)
        {
            var role = new Role { Id = user.RoleId, Name = "Role-" + user.RoleId, CreatedAt = DateTime.UtcNow };
            if (!_dbFixture.Context.Roles.Any(r => r.Id == user.RoleId))
            {
                _dbFixture.Context.Roles.Add(role);
            }

            _dbFixture.Context.Users.Add(new User
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PersonalId = user.PersonalId,
                RoleId = user.RoleId,
                CreatedAt = user.CreatedAt
            });
            _dbFixture.Context.SaveChanges();
        }

        // ───────────────────────── GetUserById ─────────────────────────

        [Fact]
        public async Task GetUserById_UserExists_ReturnsOkWithDto()
        {
            var controller = CreateController();
            var user = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await controller.GetUserById(user.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserGETResponseDTO>(ok.Value);
            Assert.Equal(user.Id, dto.Id);
            Assert.Equal("Unknown", dto.DepartmentName);
        }

        [Fact]
        public async Task GetUserById_UserMissing_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.GetUserById(Guid.NewGuid());

            Assert.IsType<NotFoundResult>(result.Result);
        }

        // ───────────────────────── GetUserByPersonalId ─────────────────────────

        [Fact]
        public async Task GetUserByPersonalId_UserExists_ReturnsOk()
        {
            var controller = CreateController();
            var user = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByPersonalIdAsync(user.PersonalId)).ReturnsAsync(user);

            var result = await controller.GetUserByPersonalId(user.PersonalId);

            Assert.IsType<OkObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetUserByPersonalId_UserMissing_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByPersonalIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await controller.GetUserByPersonalId("unknown");

            Assert.IsType<NotFoundResult>(result.Result);
        }

        // ───────────────────────── GetAllUsers ─────────────────────────

        [Fact]
        public async Task GetAllUsers_AsAdmin_ReturnsAllUsers()
        {
            var controller = CreateController();
            StubEmptyDocumentSets();
            var users = new[] { MakeUser(), MakeUser() };
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(users);

            var result = await controller.GetAllUsers();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<UserGETResponseDTO>>(ok.Value);
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task GetAllUsers_AsNonAdmin_FiltersToOwnAndReports()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController();
            controller.SetUser(callerId, role: "Basic User");
            StubEmptyDocumentSets();

            var self = MakeUser(id: callerId);
            var myReport = MakeUser(assignedToId: callerId);
            var unrelated = MakeUser();
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[] { self, myReport, unrelated });

            var result = await controller.GetAllUsers();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<UserGETResponseDTO>>(ok.Value).ToList();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, u => u.Id == self.Id);
            Assert.Contains(list, u => u.Id == myReport.Id);
            Assert.DoesNotContain(list, u => u.Id == unrelated.Id);
        }

        // ───────────────────────── GetUsersByDepartment ─────────────────────────

        [Fact]
        public async Task GetUsersByDepartment_NoUsersAndDepartmentMissing_ReturnsNotFound()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(departmentId)).ReturnsAsync(Array.Empty<User>());
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync((Department?)null);

            var result = await controller.GetUsersByDepartment(departmentId);

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetUsersByDepartment_UsersFound_ReturnsOk()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(departmentId)).ReturnsAsync(new[] { MakeUser(departmentId: departmentId) });

            var result = await controller.GetUsersByDepartment(departmentId);

            Assert.IsType<OkObjectResult>(result.Result);
        }

        // ───────────────────────── GetUsersAssignedTo ─────────────────────────

        [Fact]
        public async Task GetUsersAssignedTo_ManagerMissing_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.GetUsersAssignedTo(Guid.NewGuid());

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetUsersAssignedTo_Success_ReturnsOk()
        {
            var controller = CreateController();
            var manager = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(manager.Id)).ReturnsAsync(manager);
            _userServiceMock.Setup(s => s.GetUsersAssignedToAsync(manager.Id)).ReturnsAsync(new[] { MakeUser(assignedToId: manager.Id) });

            var result = await controller.GetUsersAssignedTo(manager.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<UserGETResponseDTO>>(ok.Value).ToList();
            Assert.Single(list);
            Assert.Contains($"{manager.FirstName} {manager.LastName}", list[0].AssignedToName);
        }

        // ───────────────────────── AddUser ─────────────────────────

        private static UserRequestDTO ValidUserRequest(Guid departmentId) => new()
        {
            FirstName = "Jane",
            LastName = "Smith",
            Email = "jane.smith@example.com",
            DepartmentId = departmentId
        };

        [Fact]
        public async Task AddUser_MissingFields_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.AddUser(new UserRequestDTO { FirstName = "", LastName = "Smith", Email = "j@e.com", DepartmentId = Guid.NewGuid() });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.False(dto.Success);
        }

        [Fact]
        public async Task AddUser_InvalidEmail_ReturnsBadRequest()
        {
            var controller = CreateController();
            var request = ValidUserRequest(Guid.NewGuid());
            request.Email = "not-an-email";

            var result = await controller.AddUser(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task AddUser_DepartmentNotFound_ReturnsBadRequest()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync((Department?)null);

            var result = await controller.AddUser(ValidUserRequest(departmentId));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("Department not found", dto.Message);
        }

        [Fact]
        public async Task AddUser_AssignedToNotFound_ReturnsBadRequest()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            var request = ValidUserRequest(departmentId);
            request.AssignedToId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(request.AssignedToId.Value)).ReturnsAsync((User?)null);

            var result = await controller.AddUser(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("Assigned to user not found", dto.Message);
        }

        [Fact]
        public async Task AddUser_MissingBasicUserRole_ReturnsBadRequest()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _userServiceMock.Setup(s => s.GetRoleIdByNameAsync("Basic User")).ReturnsAsync((Guid?)null);
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var result = await controller.AddUser(ValidUserRequest(departmentId));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("Role 'Basic User' not found", dto.Message);
        }

        [Fact]
        public async Task AddUser_WithAssignedManager_PromotesManagerToLineManager()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            var lineManagerRoleId = Guid.NewGuid();
            var basicRoleId = Guid.NewGuid();
            var manager = MakeUser();
            manager.RoleId = Guid.NewGuid(); // not yet a line manager

            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _userServiceMock.Setup(s => s.GetUserByIdAsync(manager.Id)).ReturnsAsync(manager);
            _userServiceMock.Setup(s => s.GetRoleIdByNameAsync("Basic User")).ReturnsAsync(basicRoleId);
            _userServiceMock.Setup(s => s.GetRoleIdByNameAsync("Line Manager")).ReturnsAsync(lineManagerRoleId);
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync(new Function { Id = Guid.NewGuid(), Name = "Unknown", CreatedAt = DateTime.UtcNow });

            var request = ValidUserRequest(departmentId);
            request.AssignedToId = manager.Id;

            var result = await controller.AddUser(request);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(ok.Value);
            Assert.True(dto.Success);
            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.Email == "jane.smith@example.com" && u.AssignedToId == manager.Id)), Times.Once);
            _userServiceMock.Verify(s => s.UpdateUserAsync(It.Is<User>(u => u.Id == manager.Id && u.RoleId == lineManagerRoleId)), Times.Once);
        }

        // ───────────────────────── UpdateUser ─────────────────────────

        [Fact]
        public async Task UpdateUser_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.UpdateUser(Guid.NewGuid(), ValidUserRequest(Guid.NewGuid()));

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task UpdateUser_SelfAssignment_ReturnsBadRequest()
        {
            var controller = CreateController();
            var existing = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(existing.Id)).ReturnsAsync(existing);
            var request = ValidUserRequest(Guid.NewGuid());
            request.AssignedToId = existing.Id;

            var result = await controller.UpdateUser(existing.Id, request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("cannot be assigned to themselves", dto.Message);
        }

        [Fact]
        public async Task UpdateUser_DepartmentNotFound_ReturnsBadRequest()
        {
            var controller = CreateController();
            var existing = MakeUser();
            var departmentId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(existing.Id)).ReturnsAsync(existing);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync((Department?)null);

            var result = await controller.UpdateUser(existing.Id, ValidUserRequest(departmentId));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("Department not found", dto.Message);
        }

        [Fact]
        public async Task UpdateUser_CircularReference_ReturnsBadRequest()
        {
            var controller = CreateController();
            var existing = MakeUser();
            var departmentId = Guid.NewGuid();
            var proposedManager = MakeUser(assignedToId: existing.Id); // proposed manager already reports to existing user

            _userServiceMock.Setup(s => s.GetUserByIdAsync(existing.Id)).ReturnsAsync(existing);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _userServiceMock.Setup(s => s.GetUserByIdAsync(proposedManager.Id)).ReturnsAsync(proposedManager);

            var request = ValidUserRequest(departmentId);
            request.AssignedToId = proposedManager.Id;

            var result = await controller.UpdateUser(existing.Id, request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("Circular assignment detected", dto.Message);
        }

        [Fact]
        public async Task UpdateUser_NameChanged_RecordsChangeHistory()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: departmentId);
            existing.FirstName = "OldName";

            _userServiceMock.Setup(s => s.GetUserByIdAsync(existing.Id)).ReturnsAsync(existing);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = ValidUserRequest(departmentId);
            request.FirstName = "NewName";

            var result = await controller.UpdateUser(existing.Id, request);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(ok.Value);
            Assert.True(dto.Success);
            Assert.Equal("NewName", existing.FirstName);
            _userChangeHistoryServiceMock.Verify(s => s.AddUserChangeHistoryAsync(It.Is<UserChangeHistory>(
                h => h.FieldName == "FirstName" && h.OldValue == "OldName" && h.NewValue == "NewName")), Times.Once);
        }

        // ───────────────────────── DeleteUser ─────────────────────────

        [Fact]
        public async Task DeleteUser_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.DeleteUser(Guid.NewGuid());

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task DeleteUser_Success_ReturnsOk()
        {
            var controller = CreateController();
            var user = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await controller.DeleteUser(user.Id);

            Assert.IsType<OkObjectResult>(result.Result);
            _userServiceMock.Verify(s => s.DeleteUserAsync(user.Id), Times.Once);
        }

        // ───────────────────────── GetUserSSMSUForm ─────────────────────────

        [Fact]
        public async Task GetUserSSMSUForm_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.GetUserSSMSUForm(Guid.NewGuid());

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetUserSSMSUForm_NonAdminNotOwnerOrManager_ReturnsForbidden()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController();
            controller.SetUser(callerId, role: "Basic User");
            var user = MakeUser(); // unrelated to caller

            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await controller.GetUserSSMSUForm(user.Id);

            Assert.IsType<ForbidResult>(result.Result);
        }

        [Fact]
        public async Task GetUserSSMSUForm_Owner_ReturnsOk()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController();
            controller.SetUser(callerId, role: "Basic User");
            var user = MakeUser(id: callerId);

            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);
            _periodicTrainingServiceMock.Setup(s => s.GetByUserIdAsync(user.Id)).ReturnsAsync(Array.Empty<PeriodicTrainingResponseDTO>());

            var result = await controller.GetUserSSMSUForm(user.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserSSMSUFormDTO>(ok.Value);
            Assert.Equal(user.Id, dto.Id);
        }

        // ───────────────────────── UpdateUserSSMSUForm ─────────────────────────

        [Fact]
        public async Task UpdateUserSSMSUForm_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.UpdateUserSSMSUForm(Guid.NewGuid(), new UpdateUserSSMSUFormDTO());

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task UpdateUserSSMSUForm_NonAdminNotOwnerOrManager_ReturnsForbidden()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController();
            controller.SetUser(callerId, role: "Basic User");
            var user = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await controller.UpdateUserSSMSUForm(user.Id, new UpdateUserSSMSUFormDTO());

            Assert.IsType<ForbidResult>(result.Result);
        }

        [Fact]
        public async Task UpdateUserSSMSUForm_Success_PersistsInitialTrainingRow()
        {
            var controller = CreateController();
            var user = MakeUser();
            SeedUserRow(user);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var dto = new UpdateUserSSMSUFormDTO
            {
                Address = "123 Main St",
                InitialTrainings = new List<InitialTrainingEntryDTO>
                {
                    new() { DocumentType = "ssm", IntroductoryTrainingInstructor = "Instructor A" }
                }
            };

            var result = await controller.UpdateUserSSMSUForm(user.Id, dto);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var responseDto = Assert.IsType<UserResponseDTO>(ok.Value);
            Assert.True(responseDto.Success);

            var saved = _dbFixture.Context.UserInitialTrainings.Single(t => t.UserId == user.Id && t.DocumentType == "SSM");
            Assert.Equal("Instructor A", saved.IntroductoryTrainingInstructor);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
        }

        // ───────────────────────── BulkInitialTraining ─────────────────────────

        [Fact]
        public async Task BulkInitialTraining_NoMatchingUsers_ReturnsBadRequest()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(Array.Empty<User>());

            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM" };

            var result = await controller.BulkInitialTraining(dto);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var resultDto = Assert.IsType<BulkInitialTrainingResultDTO>(badRequest.Value);
            Assert.Contains("No users found", resultDto.Errors[0]);
        }

        [Fact]
        public async Task BulkInitialTraining_Success_CreatesRowsForSelectedUsers()
        {
            var controller = CreateController();
            var user1 = MakeUser();
            var user2 = MakeUser();
            SeedUserRow(user1);
            SeedUserRow(user2);
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[] { user1, user2 });

            var dto = new BulkInitialTrainingDTO
            {
                ApplyToAllUsers = true,
                DocumentType = "SSM",
                IntroductoryTrainingInstructor = "Instructor B"
            };

            var result = await controller.BulkInitialTraining(dto);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var resultDto = Assert.IsType<BulkInitialTrainingResultDTO>(ok.Value);
            Assert.Equal(2, resultDto.SuccessCount);
            Assert.Equal(0, resultDto.FailedCount);
            Assert.Equal(2, _dbFixture.Context.UserInitialTrainings.Count(t => t.DocumentType == "SSM"));
        }
    }
}
