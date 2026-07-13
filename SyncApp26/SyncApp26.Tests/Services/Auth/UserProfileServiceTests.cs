using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Infrastructure.Repositories;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Auth
{
    public class UserProfileServiceTests : IDisposable
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<IDepartmentService> _departmentServiceMock = new();
        private readonly Mock<IFunctionService> _functionServiceMock = new();
        private readonly Mock<IUserChangeHistoryService> _userChangeHistoryServiceMock = new();
        private readonly SqliteContextFixture _dbFixture = new();

        public void Dispose() => _dbFixture.Dispose();

        private UserProfileService CreateService() =>
            new(
                _userServiceMock.Object,
                _departmentServiceMock.Object,
                _functionServiceMock.Object,
                _userChangeHistoryServiceMock.Object,
                new UserInitialTrainingService(new UserInitialTrainingRepository(_dbFixture.Context)));

        private static User MakeUser(Guid? id = null, Guid? departmentId = null, Guid? assignedToId = null, UserRole role = UserRole.BasicUser)
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
                Role = role,
                CreatedAt = DateTime.UtcNow
            };
        }

        private void SeedUserRow(User user)
        {
            _dbFixture.Context.Users.Add(new User
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PersonalId = user.PersonalId,
                Role = user.Role,
                CreatedAt = user.CreatedAt
            });
            _dbFixture.Context.SaveChanges();
        }

        private static UserRequestDTO ValidUserRequest(Guid departmentId) => new()
        {
            FirstName = "Jane",
            LastName = "Smith",
            Email = "jane.smith@example.com",
            DepartmentId = departmentId
        };

        // ───────────────────────── CreateUserAsync ─────────────────────────

        [Fact]
        public async Task CreateUserAsync_MissingFields_ReturnsFailure()
        {
            var service = CreateService();

            var result = await service.CreateUserAsync(new UserRequestDTO { FirstName = "", LastName = "Smith", Email = "j@e.com", DepartmentId = Guid.NewGuid() });

            Assert.False(result.Success);
        }

        [Fact]
        public async Task CreateUserAsync_InvalidEmail_ReturnsFailure()
        {
            var service = CreateService();
            var request = ValidUserRequest(Guid.NewGuid());
            request.Email = "not-an-email";

            var result = await service.CreateUserAsync(request);

            Assert.False(result.Success);
            Assert.Contains("Invalid email format", result.Message);
        }

        [Fact]
        public async Task CreateUserAsync_DepartmentNotFound_ReturnsFailure()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync((Department?)null);

            var result = await service.CreateUserAsync(ValidUserRequest(departmentId));

            Assert.False(result.Success);
            Assert.Contains("Department not found", result.Message);
        }

        [Fact]
        public async Task CreateUserAsync_AssignedToNotFound_ReturnsFailure()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            var request = ValidUserRequest(departmentId);
            request.AssignedToId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(request.AssignedToId.Value)).ReturnsAsync((User?)null);

            var result = await service.CreateUserAsync(request);

            Assert.False(result.Success);
            Assert.Contains("Assigned to user not found", result.Message);
        }

        [Fact]
        public async Task CreateUserAsync_WithAssignedManager_PromotesManagerToLineManager()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var manager = MakeUser(role: UserRole.BasicUser);

            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _userServiceMock.Setup(s => s.GetUserByIdAsync(manager.Id)).ReturnsAsync(manager);
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync(new Function { Id = Guid.NewGuid(), Name = "Unknown", CreatedAt = DateTime.UtcNow });

            var request = ValidUserRequest(departmentId);
            request.AssignedToId = manager.Id;

            var result = await service.CreateUserAsync(request);

            Assert.True(result.Success);
            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.Email == "jane.smith@example.com" && u.AssignedToId == manager.Id)), Times.Once);
            _userServiceMock.Verify(s => s.UpdateUserAsync(It.Is<User>(u => u.Id == manager.Id && u.Role == UserRole.LineManager)), Times.Once);
        }

        [Fact]
        public async Task CreateUserAsync_ManagerAlreadyLineManager_DoesNotUpdateManagerRole()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var manager = MakeUser(role: UserRole.LineManager);

            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _userServiceMock.Setup(s => s.GetUserByIdAsync(manager.Id)).ReturnsAsync(manager);
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = ValidUserRequest(departmentId);
            request.AssignedToId = manager.Id;

            var result = await service.CreateUserAsync(request);

            Assert.True(result.Success);
            _userServiceMock.Verify(s => s.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task CreateUserAsync_FunctionNameProvided_ResolvesFunctionByName()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var welderFunction = new Function { Id = Guid.NewGuid(), Name = "Welder", CreatedAt = DateTime.UtcNow };
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Welder")).ReturnsAsync(welderFunction);

            var request = ValidUserRequest(departmentId);
            request.Function = "Welder";

            var result = await service.CreateUserAsync(request);

            Assert.True(result.Success);
            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.FunctionId == welderFunction.Id)), Times.Once);
        }

        // ───────────────────────── UpdateUserAsync ─────────────────────────

        [Fact]
        public async Task UpdateUserAsync_SelfAssignment_ReturnsFailure()
        {
            var service = CreateService();
            var existing = MakeUser();
            var request = ValidUserRequest(Guid.NewGuid());
            request.AssignedToId = existing.Id;

            var result = await service.UpdateUserAsync(existing, request);

            Assert.False(result.Success);
            Assert.Contains("cannot be assigned to themselves", result.Message);
        }

        [Fact]
        public async Task UpdateUserAsync_DepartmentNotFound_ReturnsFailure()
        {
            var service = CreateService();
            var existing = MakeUser();
            var departmentId = Guid.NewGuid();
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync((Department?)null);

            var result = await service.UpdateUserAsync(existing, ValidUserRequest(departmentId));

            Assert.False(result.Success);
            Assert.Contains("Department not found", result.Message);
        }

        [Fact]
        public async Task UpdateUserAsync_CircularReference_ReturnsFailure()
        {
            var service = CreateService();
            var existing = MakeUser();
            var departmentId = Guid.NewGuid();
            var proposedManager = MakeUser(assignedToId: existing.Id);

            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _userServiceMock.Setup(s => s.GetUserByIdAsync(proposedManager.Id)).ReturnsAsync(proposedManager);

            var request = ValidUserRequest(departmentId);
            request.AssignedToId = proposedManager.Id;

            var result = await service.UpdateUserAsync(existing, request);

            Assert.False(result.Success);
            Assert.Contains("Circular assignment detected", result.Message);
        }

        [Fact]
        public async Task UpdateUserAsync_NameChanged_RecordsChangeHistory()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: departmentId);
            existing.FirstName = "OldName";

            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = ValidUserRequest(departmentId);
            request.FirstName = "NewName";

            var result = await service.UpdateUserAsync(existing, request);

            Assert.True(result.Success);
            Assert.Equal("NewName", existing.FirstName);
            _userChangeHistoryServiceMock.Verify(s => s.AddUserChangeHistoryAsync(It.Is<UserChangeHistory>(
                h => h.FieldName == "FirstName" && h.OldValue == "OldName" && h.NewValue == "NewName")), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_RoleProvided_UpdatesRole()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: departmentId);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = ValidUserRequest(departmentId);
            request.Role = UserRole.Admin;

            var result = await service.UpdateUserAsync(existing, request);

            Assert.True(result.Success);
            Assert.Equal(UserRole.Admin, existing.Role);
        }

        [Fact]
        public async Task UpdateUserAsync_RoleNotProvided_KeepsOriginalRole()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: departmentId, role: UserRole.LineManager);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = ValidUserRequest(departmentId);

            var result = await service.UpdateUserAsync(existing, request);

            Assert.True(result.Success);
            Assert.Equal(UserRole.LineManager, existing.Role);
        }

        [Fact]
        public async Task UpdateUserAsync_NoFieldsChanged_DoesNotRecordChangeHistory()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: departmentId);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = new UserRequestDTO
            {
                FirstName = existing.FirstName,
                LastName = existing.LastName,
                Email = existing.Email,
                DepartmentId = departmentId
            };

            await service.UpdateUserAsync(existing, request);

            _userChangeHistoryServiceMock.Verify(s => s.AddUserChangeHistoryAsync(It.IsAny<UserChangeHistory>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserAsync_DepartmentChanged_RecordsChangeHistory()
        {
            var service = CreateService();
            var oldDepartmentId = Guid.NewGuid();
            var newDepartmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: oldDepartmentId);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(newDepartmentId)).ReturnsAsync(new Department { Id = newDepartmentId, Name = "NewDept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = ValidUserRequest(newDepartmentId);

            await service.UpdateUserAsync(existing, request);

            _userChangeHistoryServiceMock.Verify(s => s.AddUserChangeHistoryAsync(It.Is<UserChangeHistory>(
                h => h.FieldName == "DepartmentName" && h.NewValue == "NewDept")), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_AssignedToChanged_RecordsChangeHistory()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: departmentId);
            var manager = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(manager.Id)).ReturnsAsync(manager);
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Unknown")).ReturnsAsync((Function?)null);

            var request = ValidUserRequest(departmentId);
            request.AssignedToId = manager.Id;

            await service.UpdateUserAsync(existing, request);

            _userChangeHistoryServiceMock.Verify(s => s.AddUserChangeHistoryAsync(It.Is<UserChangeHistory>(
                h => h.FieldName == "AssignedToName" && h.NewValue == $"{manager.FirstName} {manager.LastName}")), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_FunctionChanged_RecordsChangeHistory()
        {
            var service = CreateService();
            var departmentId = Guid.NewGuid();
            var existing = MakeUser(departmentId: departmentId);
            var newFunction = new Function { Id = Guid.NewGuid(), Name = "Welder", CreatedAt = DateTime.UtcNow };
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });
            _functionServiceMock.Setup(s => s.GetByNameAsync("Welder")).ReturnsAsync(newFunction);

            var request = ValidUserRequest(departmentId);
            request.Function = "Welder";

            await service.UpdateUserAsync(existing, request);

            _userChangeHistoryServiceMock.Verify(s => s.AddUserChangeHistoryAsync(It.Is<UserChangeHistory>(
                h => h.FieldName == "FunctionName" && h.NewValue == "Welder")), Times.Once);
        }

        // ───────────────────────── UpdateSsmSuFormAsync ─────────────────────────

        [Fact]
        public async Task UpdateSsmSuFormAsync_NewEntry_CreatesInitialTrainingRow()
        {
            var service = CreateService();
            var user = MakeUser();
            SeedUserRow(user);

            var dto = new UpdateUserSSMSUFormDTO
            {
                Address = "123 Main St",
                InitialTrainings = new List<InitialTrainingEntryDTO>
                {
                    new() { DocumentType = "ssm", IntroductoryTrainingInstructor = "Instructor A" }
                }
            };

            await service.UpdateSsmSuFormAsync(user, dto);

            Assert.Equal("123 Main St", user.Address);
            var saved = _dbFixture.Context.UserInitialTrainings.Single(t => t.UserId == user.Id && t.DocumentType == "SSM");
            Assert.Equal("Instructor A", saved.IntroductoryTrainingInstructor);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
        }

        [Fact]
        public async Task UpdateSsmSuFormAsync_ExistingRow_UpdatesInPlaceRatherThanDuplicating()
        {
            var service = CreateService();
            var user = MakeUser();
            SeedUserRow(user);
            var existingRow = new UserInitialTraining
            {
                UserId = user.Id,
                DocumentType = "SSM",
                IntroductoryTrainingInstructor = "Old Instructor",
                CreatedAt = DateTime.UtcNow
            };
            _dbFixture.Context.UserInitialTrainings.Add(existingRow);
            _dbFixture.Context.SaveChanges();

            var dto = new UpdateUserSSMSUFormDTO
            {
                InitialTrainings = new List<InitialTrainingEntryDTO>
                {
                    new() { DocumentType = "SSM", IntroductoryTrainingInstructor = "New Instructor" }
                }
            };

            await service.UpdateSsmSuFormAsync(user, dto);

            var rows = _dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == user.Id).ToList();
            Assert.Single(rows);
            Assert.Equal(existingRow.Id, rows[0].Id);
            Assert.Equal("New Instructor", rows[0].IntroductoryTrainingInstructor);
        }

        [Fact]
        public async Task UpdateSsmSuFormAsync_BlankDocumentType_SkipsEntry()
        {
            var service = CreateService();
            var user = MakeUser();

            var dto = new UpdateUserSSMSUFormDTO
            {
                InitialTrainings = new List<InitialTrainingEntryDTO>
                {
                    new() { DocumentType = "", IntroductoryTrainingInstructor = "Should Be Ignored" }
                }
            };

            await service.UpdateSsmSuFormAsync(user, dto);

            Assert.Empty(_dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == user.Id));
        }

        // ───────────────────────── ApplyBulkInitialTrainingAsync ─────────────────────────

        [Fact]
        public async Task ApplyBulkInitialTrainingAsync_NoMatchingUsers_ReturnsNoUsersMatched()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(Array.Empty<User>());

            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM" };

            var result = await service.ApplyBulkInitialTrainingAsync(dto, null);

            Assert.True(result.NoUsersMatched);
            Assert.Contains("No users found", result.Errors[0]);
        }

        [Fact]
        public async Task ApplyBulkInitialTrainingAsync_Success_CreatesRowsForSelectedUsers()
        {
            var service = CreateService();
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

            var result = await service.ApplyBulkInitialTrainingAsync(dto, null);

            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(2, _dbFixture.Context.UserInitialTrainings.Count(t => t.DocumentType == "SSM"));
        }

        [Fact]
        public async Task ApplyBulkInitialTrainingAsync_SelectedDepartmentFilter_OnlyAppliesToThatDepartment()
        {
            var service = CreateService();
            var targetDeptId = Guid.NewGuid();
            var otherDeptId = Guid.NewGuid();
            var inDept = MakeUser(departmentId: targetDeptId);
            var outOfDept = MakeUser(departmentId: otherDeptId);
            SeedUserRow(inDept);
            SeedUserRow(outOfDept);
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[] { inDept, outOfDept });

            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM", SelectedDepartmentId = targetDeptId };

            var result = await service.ApplyBulkInitialTrainingAsync(dto, null);

            Assert.Equal(1, result.SuccessCount);
            Assert.Single(_dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == inDept.Id));
            Assert.Empty(_dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == outOfDept.Id));
        }

        [Fact]
        public async Task ApplyBulkInitialTrainingAsync_ApplyToAllUsersFalse_OnlyAppliesToSelectedUserIds()
        {
            var service = CreateService();
            var selectedUser = MakeUser();
            var unselectedUser = MakeUser();
            SeedUserRow(selectedUser);
            SeedUserRow(unselectedUser);
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[] { selectedUser, unselectedUser });

            var dto = new BulkInitialTrainingDTO
            {
                ApplyToAllUsers = false,
                DocumentType = "SSM",
                SelectedUserIds = new List<Guid> { selectedUser.Id }
            };

            var result = await service.ApplyBulkInitialTrainingAsync(dto, null);

            Assert.Equal(1, result.SuccessCount);
            Assert.Single(_dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == selectedUser.Id));
            Assert.Empty(_dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == unselectedUser.Id));
        }

        [Fact]
        public async Task ApplyBulkInitialTrainingAsync_BothDocumentType_CreatesSsmAndSuRows()
        {
            var service = CreateService();
            var user = MakeUser();
            SeedUserRow(user);
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[] { user });

            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "Both" };

            var result = await service.ApplyBulkInitialTrainingAsync(dto, null);

            Assert.Equal(1, result.SuccessCount);
            var rows = _dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == user.Id).Select(t => t.DocumentType).ToList();
            Assert.Contains("SSM", rows);
            Assert.Contains("SU", rows);
        }

        [Fact]
        public async Task ApplyBulkInitialTrainingAsync_RestrictToAssignedToId_OnlyAppliesToThoseUsers()
        {
            var service = CreateService();
            var managerId = Guid.NewGuid();
            var myEmployee = MakeUser(assignedToId: managerId);
            var otherEmployee = MakeUser();
            SeedUserRow(myEmployee);
            SeedUserRow(otherEmployee);
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[] { myEmployee, otherEmployee });

            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM" };

            var result = await service.ApplyBulkInitialTrainingAsync(dto, managerId);

            Assert.Equal(1, result.SuccessCount);
            Assert.Single(_dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == myEmployee.Id));
            Assert.Empty(_dbFixture.Context.UserInitialTrainings.Where(t => t.UserId == otherEmployee.Id));
        }

        [Fact]
        public async Task ApplyBulkInitialTrainingAsync_ExistingNonEmptyFields_ArePreservedAndCountedAsSkipped()
        {
            var service = CreateService();
            var user = MakeUser();
            SeedUserRow(user);
            _dbFixture.Context.UserInitialTrainings.Add(new UserInitialTraining
            {
                UserId = user.Id,
                DocumentType = "SSM",
                IntroductoryTrainingInstructor = "Original Instructor",
                CreatedAt = DateTime.UtcNow
            });
            _dbFixture.Context.SaveChanges();
            _userServiceMock.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[] { user });

            var dto = new BulkInitialTrainingDTO
            {
                ApplyToAllUsers = true,
                DocumentType = "SSM",
                IntroductoryTrainingInstructor = "New Instructor"
            };

            var result = await service.ApplyBulkInitialTrainingAsync(dto, null);

            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(1, result.SkippedCount);
            var row = _dbFixture.Context.UserInitialTrainings.Single(t => t.UserId == user.Id && t.DocumentType == "SSM");
            Assert.Equal("Original Instructor", row.IntroductoryTrainingInstructor);
        }
    }
}
