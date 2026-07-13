using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.PeriodicTraining;
using SyncApp26.Shared.DTOs.Response.User;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Auth
{
    public class UserControllerTests
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<IDepartmentService> _departmentServiceMock = new();
        private readonly Mock<IDocumentService> _documentServiceMock = new();
        private readonly Mock<IPeriodicTrainingService> _periodicTrainingServiceMock = new();
        private readonly Mock<IUserProfileService> _userProfileServiceMock = new();

        private UserController CreateController()
        {
            var controller = new UserController(
                _userServiceMock.Object,
                _departmentServiceMock.Object,
                _documentServiceMock.Object,
                _periodicTrainingServiceMock.Object,
                _userProfileServiceMock.Object);

            controller.SetUser(Guid.NewGuid(), role: Roles.Admin);
            return controller;
        }

        private void StubEmptyDocumentSets()
        {
            _documentServiceMock.Setup(s => s.GetUserIdsWithDocumentTypeAsync(It.IsAny<string>())).ReturnsAsync(new HashSet<Guid>());
            _documentServiceMock.Setup(s => s.GetUserIdsWithUnsignedDocumentTypeAsync(It.IsAny<string>())).ReturnsAsync(new HashSet<Guid>());
        }

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
            controller.SetUser(callerId, role: Roles.BasicUser);
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

        [Fact]
        public async Task GetUsersByDepartment_NoUsersButDepartmentExists_ReturnsOkEmptyList()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUsersByDepartmentIdAsync(departmentId)).ReturnsAsync(Array.Empty<User>());
            _departmentServiceMock.Setup(s => s.GetDepartmentByIdAsync(departmentId)).ReturnsAsync(new Department { Id = departmentId, Name = "Dept", CreatedAt = DateTime.UtcNow });

            var result = await controller.GetUsersByDepartment(departmentId);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var list = Assert.IsAssignableFrom<IEnumerable<UserGETResponseDTO>>(ok.Value);
            Assert.Empty(list);
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
        public async Task AddUser_ServiceReportsFailure_ReturnsBadRequest()
        {
            var controller = CreateController();
            _userProfileServiceMock.Setup(s => s.CreateUserAsync(It.IsAny<UserRequestDTO>()))
                .ReturnsAsync(new UserResponseDTO { Success = false, Message = "Department not found" });

            var result = await controller.AddUser(ValidUserRequest(Guid.NewGuid()));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("Department not found", dto.Message);
        }

        [Fact]
        public async Task AddUser_ServiceReportsSuccess_ReturnsOk()
        {
            var controller = CreateController();
            var request = ValidUserRequest(Guid.NewGuid());
            _userProfileServiceMock.Setup(s => s.CreateUserAsync(request))
                .ReturnsAsync(new UserResponseDTO { Success = true, Message = "User created successfully" });

            var result = await controller.AddUser(request);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(ok.Value);
            Assert.True(dto.Success);
            _userProfileServiceMock.Verify(s => s.CreateUserAsync(request), Times.Once);
        }

        // ───────────────────────── UpdateUser ─────────────────────────

        [Fact]
        public async Task UpdateUser_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.UpdateUser(Guid.NewGuid(), ValidUserRequest(Guid.NewGuid()));

            Assert.IsType<NotFoundObjectResult>(result.Result);
            _userProfileServiceMock.Verify(s => s.UpdateUserAsync(It.IsAny<User>(), It.IsAny<UserRequestDTO>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUser_ServiceReportsFailure_ReturnsBadRequest()
        {
            var controller = CreateController();
            var existing = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(existing.Id)).ReturnsAsync(existing);
            _userProfileServiceMock.Setup(s => s.UpdateUserAsync(existing, It.IsAny<UserRequestDTO>()))
                .ReturnsAsync(new UserResponseDTO { Success = false, Message = "Circular assignment detected: Cannot assign a user to someone who reports to them" });

            var result = await controller.UpdateUser(existing.Id, ValidUserRequest(Guid.NewGuid()));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(badRequest.Value);
            Assert.Contains("Circular assignment detected", dto.Message);
        }

        [Fact]
        public async Task UpdateUser_ServiceReportsSuccess_ReturnsOk()
        {
            var controller = CreateController();
            var existing = MakeUser();
            var request = ValidUserRequest(Guid.NewGuid());
            _userServiceMock.Setup(s => s.GetUserByIdAsync(existing.Id)).ReturnsAsync(existing);
            _userProfileServiceMock.Setup(s => s.UpdateUserAsync(existing, request))
                .ReturnsAsync(new UserResponseDTO { Success = true, Message = "User updated successfully" });

            var result = await controller.UpdateUser(existing.Id, request);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserResponseDTO>(ok.Value);
            Assert.True(dto.Success);
            _userProfileServiceMock.Verify(s => s.UpdateUserAsync(existing, request), Times.Once);
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
            controller.SetUser(callerId, role: Roles.BasicUser);
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
            controller.SetUser(callerId, role: Roles.BasicUser);
            var user = MakeUser(id: callerId);

            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);
            _periodicTrainingServiceMock.Setup(s => s.GetByUserIdAsync(user.Id)).ReturnsAsync(Array.Empty<PeriodicTrainingResponseDTO>());

            var result = await controller.GetUserSSMSUForm(user.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserSSMSUFormDTO>(ok.Value);
            Assert.Equal(user.Id, dto.Id);
        }

        [Fact]
        public async Task GetUserSSMSUForm_WithTrainingsAndInitialTrainings_MapsLatestAndInitialTrainings()
        {
            var controller = CreateController();
            var user = MakeUser();
            user.InitialTrainings.Add(new UserInitialTraining
            {
                UserId = user.Id,
                DocumentType = "SSM",
                IntroductoryTrainingInstructor = "Jane"
            });
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var older = new PeriodicTrainingResponseDTO
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TrainingDate = DateTime.UtcNow.AddDays(-10),
                InstructorSignature = "old-signature"
            };
            var newer = new PeriodicTrainingResponseDTO
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TrainingDate = DateTime.UtcNow.AddDays(-1),
                InstructorSignature = "new-signature",
                InstructorSignatureMethod = "Draw"
            };
            _periodicTrainingServiceMock.Setup(s => s.GetByUserIdAsync(user.Id)).ReturnsAsync(new[] { older, newer });

            var result = await controller.GetUserSSMSUForm(user.Id);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserSSMSUFormDTO>(ok.Value);
            Assert.Equal("new-signature", dto.LatestInstructorSignature);
            Assert.Equal("Draw", dto.LatestInstructorSignatureMethod);
            Assert.Single(dto.InitialTrainings);
            Assert.Equal("Jane", dto.InitialTrainings[0].IntroductoryTrainingInstructor);
        }

        // ───────────────────────── UpdateUserSSMSUForm ─────────────────────────

        [Fact]
        public async Task UpdateUserSSMSUForm_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var result = await controller.UpdateUserSSMSUForm(Guid.NewGuid(), new UpdateUserSSMSUFormDTO());

            Assert.IsType<NotFoundObjectResult>(result.Result);
            _userProfileServiceMock.Verify(s => s.UpdateSsmSuFormAsync(It.IsAny<User>(), It.IsAny<UpdateUserSSMSUFormDTO>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserSSMSUForm_NonAdminNotOwnerOrManager_ReturnsForbidden()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController();
            controller.SetUser(callerId, role: Roles.BasicUser);
            var user = MakeUser();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await controller.UpdateUserSSMSUForm(user.Id, new UpdateUserSSMSUFormDTO());

            Assert.IsType<ForbidResult>(result.Result);
            _userProfileServiceMock.Verify(s => s.UpdateSsmSuFormAsync(It.IsAny<User>(), It.IsAny<UpdateUserSSMSUFormDTO>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserSSMSUForm_Success_DelegatesToService()
        {
            var controller = CreateController();
            var user = MakeUser();
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
            _userProfileServiceMock.Verify(s => s.UpdateSsmSuFormAsync(user, dto), Times.Once);
        }

        // ───────────────────────── BulkInitialTraining ─────────────────────────

        [Fact]
        public async Task BulkInitialTraining_NoUsersMatched_ReturnsBadRequest()
        {
            var controller = CreateController();
            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM" };
            var resultDto = new BulkInitialTrainingResultDTO { NoUsersMatched = true };
            resultDto.Errors.Add("No users found to apply initial training data.");
            _userProfileServiceMock.Setup(s => s.ApplyBulkInitialTrainingAsync(dto, null)).ReturnsAsync(resultDto);

            var result = await controller.BulkInitialTraining(dto);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var returnedDto = Assert.IsType<BulkInitialTrainingResultDTO>(badRequest.Value);
            Assert.Contains("No users found", returnedDto.Errors[0]);
        }

        [Fact]
        public async Task BulkInitialTraining_Success_ReturnsOk()
        {
            var controller = CreateController();
            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM" };
            var resultDto = new BulkInitialTrainingResultDTO { SuccessCount = 2 };
            _userProfileServiceMock.Setup(s => s.ApplyBulkInitialTrainingAsync(dto, null)).ReturnsAsync(resultDto);

            var result = await controller.BulkInitialTraining(dto);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var returnedDto = Assert.IsType<BulkInitialTrainingResultDTO>(ok.Value);
            Assert.Equal(2, returnedDto.SuccessCount);
        }

        [Fact]
        public async Task BulkInitialTraining_NonAdmin_RestrictsToCallerId()
        {
            var managerId = Guid.NewGuid();
            var controller = CreateController();
            controller.SetUser(managerId, role: Roles.LineManager);

            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM" };
            _userProfileServiceMock.Setup(s => s.ApplyBulkInitialTrainingAsync(dto, managerId)).ReturnsAsync(new BulkInitialTrainingResultDTO { SuccessCount = 1 });

            var result = await controller.BulkInitialTraining(dto);

            Assert.IsType<OkObjectResult>(result.Result);
            _userProfileServiceMock.Verify(s => s.ApplyBulkInitialTrainingAsync(dto, managerId), Times.Once);
        }

        [Fact]
        public async Task BulkInitialTraining_Admin_PassesNullRestriction()
        {
            var controller = CreateController();
            var dto = new BulkInitialTrainingDTO { ApplyToAllUsers = true, DocumentType = "SSM" };
            _userProfileServiceMock.Setup(s => s.ApplyBulkInitialTrainingAsync(dto, null)).ReturnsAsync(new BulkInitialTrainingResultDTO { SuccessCount = 1 });

            var result = await controller.BulkInitialTraining(dto);

            Assert.IsType<OkObjectResult>(result.Result);
            _userProfileServiceMock.Verify(s => s.ApplyBulkInitialTrainingAsync(dto, null), Times.Once);
        }
    }
}
