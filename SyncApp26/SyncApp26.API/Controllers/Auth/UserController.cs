using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.User;
using SyncApp26.API.Extensions;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IDepartmentService _departmentService;
        private readonly IDocumentService _documentService;
        private readonly IPeriodicTrainingService _periodicTrainingService;
        private readonly IUserProfileService _userProfileService;

        public UserController(IUserService userService, IDepartmentService departmentService, IDocumentService documentService, IPeriodicTrainingService periodicTrainingService, IUserProfileService userProfileService)
        {
            _userService = userService;
            _departmentService = departmentService;
            _documentService = documentService;
            _periodicTrainingService = periodicTrainingService;
            _userProfileService = userProfileService;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserGETResponseDTO>> GetUserById(Guid id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            return Ok(new UserGETResponseDTO
            {
                Id = user.Id,
                PersonalId = user.PersonalId,
                Role = user.Role,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
                Function = user.Function?.Name ?? "Unknown",
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpGet("personal-id/{personalId}")]
        public async Task<ActionResult<UserGETResponseDTO>> GetUserByPersonalId(string personalId)
        {
            var user = await _userService.GetUserByPersonalIdAsync(personalId);
            if (user == null)
            {
                return NotFound();
            }

            return Ok(new UserGETResponseDTO
            {
                Id = user.Id,
                PersonalId = user.PersonalId,
                Role = user.Role,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
                Function = user.Function?.Name ?? "Unknown",
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserGETResponseDTO>>> GetAllUsers()
        {
            var usersList = await _userService.GetAllUsersAsync();
            var users = usersList.AsEnumerable();

            var isAdmin = User.IsInRole(Roles.Admin);
            if (!isAdmin && User.GetUserId() is { } currentUserId)
            {
                users = users.Where(u => u.AssignedToId == currentUserId || u.Id == currentUserId);
            }

            var ssmIds = await _documentService.GetUserIdsWithDocumentTypeAsync("SSM");
            var suIds = await _documentService.GetUserIdsWithDocumentTypeAsync("SU");
            var unsignedSsmIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync("SSM");
            var unsignedSuIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync("SU");

            var responseList = users.Select(user => new UserGETResponseDTO
            {
                Id = user.Id,
                PersonalId = user.PersonalId,
                Role = user.Role,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
                Function = user.Function?.Name ?? "Unknown",
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                HasSignedSsm = ssmIds.Contains(user.Id),
                HasSignedSu = suIds.Contains(user.Id),
                HasUnsignedSsm = unsignedSsmIds.Contains(user.Id),
                HasUnsignedSu = unsignedSuIds.Contains(user.Id)
            }).ToList();

            return Ok(responseList);
        }

        [HttpGet("department/{departmentId}")]
        public async Task<ActionResult<IEnumerable<UserGETResponseDTO>>> GetUsersByDepartment(Guid departmentId)
        {
            var users = await _userService.GetUsersByDepartmentIdAsync(departmentId);
            var usersList = users.ToList();

            if (!usersList.Any())
            {
                var department = await _departmentService.GetDepartmentByIdAsync(departmentId);
                if (department == null)
                {
                    return NotFound(new { message = "Department not found" });
                }
            }

            var responseList = usersList.Select(user => new UserGETResponseDTO
            {
                Id = user.Id,
                PersonalId = user.PersonalId,
                Role = user.Role,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
                Function = user.Function?.Name ?? "Unknown",
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            }).ToList();

            return Ok(responseList);
        }

        [HttpGet("assigned-to/{assignedToId}")]
        public async Task<ActionResult<IEnumerable<UserGETResponseDTO>>> GetUsersAssignedTo(Guid assignedToId)
        {
            var lineManager = await _userService.GetUserByIdAsync(assignedToId);
            if (lineManager == null)
            {
                return NotFound(new { message = "Line manager not found" });
            }

            var users = await _userService.GetUsersAssignedToAsync(assignedToId);
            var responseList = users.Select(user => new UserGETResponseDTO
            {
                Id = user.Id,
                PersonalId = user.PersonalId,
                Role = user.Role,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = $"{lineManager.FirstName} {lineManager.LastName}",
                Function = user.Function?.Name ?? "Unknown",
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            }).ToList();

            return Ok(responseList);
        }

        [HttpPost]
        public async Task<ActionResult<UserResponseDTO>> AddUser([FromBody] UserRequestDTO userRequestDTO)
        {
            var result = await _userProfileService.CreateUserAsync(userRequestDTO);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<UserResponseDTO>> UpdateUser(Guid id, [FromBody] UserRequestDTO userRequestDTO)
        {
            var existingUser = await _userService.GetUserByIdAsync(id);
            if (existingUser == null)
            {
                return NotFound(new UserResponseDTO
                {
                    Success = false,
                    Message = "User not found"
                });
            }

            var result = await _userProfileService.UpdateUserAsync(existingUser, userRequestDTO);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<UserResponseDTO>> DeleteUser(Guid id)
        {
            var existingUser = await _userService.GetUserByIdAsync(id);
            if (existingUser == null)
            {
                return NotFound(new UserResponseDTO
                {
                    Success = false,
                    Message = "User not found"
                });
            }

            await _userService.DeleteUserAsync(id);

            return Ok(new UserResponseDTO
            {
                Success = true,
                Message = "User deleted successfully"
            });
        }

        [HttpGet("{id}/ssm-su-form")]
        public async Task<ActionResult<UserSSMSUFormDTO>> GetUserSSMSUForm(Guid id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "User not found" });
            }

            bool isAdmin = User.IsInRole(Roles.Admin);
            if (!isAdmin && User.GetUserId() is { } currentUserId)
            {
                if (user.AssignedToId != currentUserId && user.Id != currentUserId)
                {
                    return Forbid();
                }
            }

            // Keep first-employment training fields sourced only from user profile data.
            var periodicTrainings = await _periodicTrainingService.GetByUserIdAsync(id);
            var latestTraining = periodicTrainings
                .OrderByDescending(pt => pt.TrainingDate)
                .ThenByDescending(pt => pt.Id)
                .FirstOrDefault();

            return Ok(new UserSSMSUFormDTO
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PersonalId = user.PersonalId,
                DepartmentName = user.Department?.Name,
                FunctionName = user.Function?.Name,
                Role = user.Role,
                ManagerFirstName = user.AssignedTo?.FirstName,
                ManagerLastName = user.AssignedTo?.LastName,
                ManagerFunctionName = user.AssignedTo?.Function?.Name,
                DateOfBirth = user.DateOfBirth,
                PlaceOfBirth = user.PlaceOfBirth,
                Address = user.Address,
                BloodGroup = user.BloodGroup,
                BadgeNumber = user.BadgeNumber,
                Education = user.Education,
                Qualifications = user.Qualifications,
                CommuteRoute = user.CommuteRoute,
                CommuteDurationMinutes = user.CommuteDurationMinutes,
                InitialTrainings = user.InitialTrainings.Select(it => new InitialTrainingEntryDTO
                {
                    DocumentType = it.DocumentType,
                    IntroductoryTrainingDate = it.IntroductoryTrainingDate,
                    IntroductoryTrainingHours = it.IntroductoryTrainingHours,
                    IntroductoryTrainingInstructor = it.IntroductoryTrainingInstructor,
                    IntroductoryTrainingInstructorFunction = it.IntroductoryTrainingInstructorFunction,
                    IntroductoryTrainingContent = it.IntroductoryTrainingContent,
                    WorkplaceTrainingDate = it.WorkplaceTrainingDate,
                    WorkplaceTrainingLocation = it.WorkplaceTrainingLocation,
                    WorkplaceTrainingHours = it.WorkplaceTrainingHours,
                    WorkplaceTrainingInstructor = it.WorkplaceTrainingInstructor,
                    WorkplaceTrainingInstructorFunction = it.WorkplaceTrainingInstructorFunction,
                    WorkplaceTrainingContent = it.WorkplaceTrainingContent,
                }).ToList(),
                AdmittedByName = user.AdmittedByName,
                AdmittedByFunction = user.AdmittedByFunction,
                AdmittedDate = user.AdmittedDate,
                HireDate = user.CreatedAt,
                CreatedAt = user.CreatedAt,
                LatestInstructorSignature = latestTraining?.InstructorSignature,
                LatestInstructorSignatureMethod = latestTraining?.InstructorSignatureMethod,
                LatestVerifierSignature = latestTraining?.VerifierSignature,
                LatestVerifierSignatureMethod = latestTraining?.VerifierSignatureMethod
            });
        }

        [HttpPut("{id}/ssm-su-form")]
        public async Task<ActionResult<UserResponseDTO>> UpdateUserSSMSUForm(Guid id, [FromBody] UpdateUserSSMSUFormDTO dto)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new UserResponseDTO
                {
                    Success = false,
                    Message = "User not found"
                });
            }

            bool isAdmin = User.IsInRole(Roles.Admin);
            if (!isAdmin && User.GetUserId() is { } currentUserId)
            {
                if (user.AssignedToId != currentUserId && user.Id != currentUserId)
                {
                    return Forbid();
                }
            }

            await _userProfileService.UpdateSsmSuFormAsync(user, dto);

            return Ok(new UserResponseDTO
            {
                Success = true,
                Message = "SSM/SU form updated successfully"
            });
        }

        [HttpPost("bulk-initial-training")]
        public async Task<ActionResult<BulkInitialTrainingResultDTO>> BulkInitialTraining([FromBody] BulkInitialTrainingDTO dto)
        {
            var isAdmin = User.IsInRole(Roles.Admin);
            Guid? restrictToAssignedToId = null;
            if (!isAdmin && User.GetUserId() is { } currentUserId)
            {
                restrictToAssignedToId = currentUserId;
            }

            var result = await _userProfileService.ApplyBulkInitialTrainingAsync(dto, restrictToAssignedToId);
            return result.NoUsersMatched ? BadRequest(result) : Ok(result);
        }
    }
}
