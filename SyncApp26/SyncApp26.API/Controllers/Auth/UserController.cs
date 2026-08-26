using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
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

        private static UserGETResponseDTO MapToUserGETResponseDTO(User user) => new UserGETResponseDTO
        {
            Id = user.Id,
            PersonalId = user.PersonalId,
            Roles = user.RoleAssignments.Select(a => a.Role.Name).ToList(),
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            DepartmentId = user.DepartmentId ?? Guid.Empty,
            DepartmentName = user.Department?.Name ?? "Unknown",
            AssignedToId = user.AssignedToId,
            AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
            Function = user.Function?.Name ?? "Unknown",
            WorkSiteId = user.WorkSiteId,
            WorkSite = user.WorkSite?.Name,
            Address = user.Address,
            BadgeNumber = user.BadgeNumber,
            BloodType = user.BloodType,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };

        // Admin and SSM/SU officers see every employee; everyone else may only reach their own
        // profile or their direct reports'. Same idiom GetAllUsers already uses.
        private bool CanAccessUser(User target)
        {
            if (User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer))
            {
                return true;
            }

            var currentUserId = User.GetUserId();
            return currentUserId != null && (target.AssignedToId == currentUserId || target.Id == currentUserId);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserGETResponseDTO>> GetUserById(Guid id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (!CanAccessUser(user))
            {
                return Forbid();
            }

            return Ok(MapToUserGETResponseDTO(user));
        }

        [HttpGet("personal-id/{personalId}")]
        public async Task<ActionResult<UserGETResponseDTO>> GetUserByPersonalId(string personalId)
        {
            var user = await _userService.GetUserByPersonalIdAsync(personalId);
            if (user == null)
            {
                return NotFound();
            }

            if (!CanAccessUser(user))
            {
                return Forbid();
            }

            return Ok(MapToUserGETResponseDTO(user));
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserGETResponseDTO>>> GetAllUsers()
        {
            var usersList = await _userService.GetAllUsersIncludingAdminsAsync();
            var users = usersList.AsEnumerable();

            // Admin and SSM/SU officers see every employee — an officer's duty spans all of them,
            // not just their own reports. Everyone else (line managers, basic users) is scoped down.
            bool seesEverything = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);
            if (!seesEverything && User.GetUserId() is { } currentUserId)
            {
                users = users.Where(u => u.AssignedToId == currentUserId || u.Id == currentUserId);
            }

            var ssmIds = await _documentService.GetUserIdsWithDocumentTypeAsync("SSM");
            var suIds = await _documentService.GetUserIdsWithDocumentTypeAsync("SU");
            var unsignedSsmIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync("SSM");
            var unsignedSuIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync("SU");

            var responseList = users.Select(user =>
            {
                var dto = MapToUserGETResponseDTO(user);
                dto.HasSignedSsm = ssmIds.Contains(user.Id);
                dto.HasSignedSu = suIds.Contains(user.Id);
                dto.HasUnsignedSsm = unsignedSsmIds.Contains(user.Id);
                dto.HasUnsignedSu = unsignedSuIds.Contains(user.Id);
                return dto;
            }).ToList();

            return Ok(responseList);
        }

        /// <summary>
        /// Paginated user search for dropdowns (e.g. selecting an instructor when generating
        /// documents). Unlike GET /User, this skips the per-user document-status mapping.
        /// </summary>
        [HttpGet("lookup")]
        public async Task<ActionResult<UserLookupPageDTO>> LookupUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (items, totalCount) = await _userService.SearchUsersAsync(search, page, pageSize);

            bool seesEverything = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);
            if (!seesEverything)
            {
                items = items.Where(CanAccessUser).ToList();
                totalCount = items.Count;
            }

            return Ok(new UserLookupPageDTO
            {
                TotalCount = totalCount,
                Items = items.Select(u => new UserLookupResponseDTO
                {
                    Id = u.Id,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Email = u.Email,
                    DepartmentName = u.Department?.Name
                }).ToList()
            });
        }

        [HttpGet("department/{departmentId}")]
        public async Task<ActionResult<IEnumerable<UserGETResponseDTO>>> GetUsersByDepartment(Guid departmentId)
        {
            var users = await _userService.GetUsersByDepartmentIdAsync(departmentId);
            var usersList = users.ToList();

            bool seesEverything = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);
            if (!seesEverything)
            {
                usersList = usersList.Where(CanAccessUser).ToList();
            }

            if (!usersList.Any())
            {
                var department = await _departmentService.GetDepartmentByIdAsync(departmentId);
                if (department == null)
                {
                    return NotFound(new { message = "Department not found" });
                }
            }

            var responseList = usersList.Select(MapToUserGETResponseDTO).ToList();

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

            bool seesEverything = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);
            if (!seesEverything && User.GetUserId() != assignedToId)
            {
                return Forbid();
            }

            var users = await _userService.GetUsersAssignedToAsync(assignedToId);
            var responseList = users.Select(user =>
            {
                var dto = MapToUserGETResponseDTO(user);
                dto.AssignedToName = $"{lineManager.FirstName} {lineManager.LastName}";
                return dto;
            }).ToList();

            return Ok(responseList);
        }

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<UserResponseDTO>> AddUser([FromBody] UserRequestDTO userRequestDTO)
        {
            var result = await _userProfileService.CreateUserAsync(userRequestDTO, User.GetUserId());
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPut("{id}/roles")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<UserResponseDTO>> SetUserRoles(Guid id, [FromBody] SetUserRolesRequestDTO request)
        {
            if (User.GetUserId() is not { } adminId)
            {
                return Unauthorized();
            }

            var result = await _userProfileService.SetUserRolesAsync(id, request.RoleNames, adminId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPatch("language")]
        public async Task<ActionResult<UserResponseDTO>> UpdateLanguagePreference([FromBody] UpdateLanguagePreferenceRequestDTO request)
        {
            if (User.GetUserId() is not { } userId)
            {
                return Unauthorized();
            }

            var result = await _userProfileService.UpdatePreferredLanguageAsync(userId, request.Language);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = Roles.Admin + "," + Roles.LineManager)]
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

            if (!User.IsInRole(Roles.Admin))
            {
                if (existingUser.AssignedToId != User.GetUserId())
                {
                    return Forbid();
                }

                // Email changes are admin-only on this route — combined with a line manager's own
                // ownership reach, changing a report's email would otherwise open an account-takeover
                // path via the anonymous forgot-password flow.
                if (!string.Equals(existingUser.Email, userRequestDTO.Email, StringComparison.Ordinal))
                {
                    return Forbid();
                }
            }

            var result = await _userProfileService.UpdateUserAsync(existingUser, userRequestDTO);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = Roles.Admin + "," + Roles.LineManager)]
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

            if (!User.IsInRole(Roles.Admin) && existingUser.AssignedToId != User.GetUserId())
            {
                return Forbid();
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

            bool seesEverything = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);
            if (!seesEverything && User.GetUserId() is { } currentUserId)
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
                WorkSiteName = user.WorkSite?.Name,
                FunctionName = user.Function?.Name,
                Roles = user.RoleAssignments.Select(a => a.Role.Name).ToList(),
                ManagerFirstName = user.AssignedTo?.FirstName,
                ManagerLastName = user.AssignedTo?.LastName,
                ManagerFunctionName = user.AssignedTo?.Function?.Name,
                DateOfBirth = user.DateOfBirth,
                PlaceOfBirth = user.PlaceOfBirth,
                Address = user.Address,
                BloodType = user.BloodType,
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

            bool seesEverything = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer);
            if (!seesEverything && User.GetUserId() is { } currentUserId)
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
            var requestedTypes = (dto.DocumentType ?? "Both").Equals("Both", StringComparison.OrdinalIgnoreCase)
                ? new[] { "SSM", "SU" }
                : new[] { dto.DocumentType!.ToUpperInvariant() };

            // Each type is authorized independently: the officer for that type can apply for anyone;
            // a line manager (with no officer duty on that type) is restricted to their own direct
            // reports; anyone else is dropped from this request rather than failing it outright.
            bool isLineManager = User.IsInRole(Roles.LineManager);
            var currentUserId = User.GetUserId();
            var includedTypes = new List<(string Type, Guid? RestrictToAssignedToId)>();
            foreach (var type in requestedTypes)
            {
                if (User.CanInitiateFor(type))
                    includedTypes.Add((type, null));
                else if (isLineManager && currentUserId is { } managerId)
                    includedTypes.Add((type, managerId));
            }

            if (includedTypes.Count == 0)
                return Forbid();

            BulkInitialTrainingResultDTO result;
            if (includedTypes.Select(t => t.RestrictToAssignedToId).Distinct().Count() == 1)
            {
                // Every included type shares the same restriction — one call, same as before.
                dto.DocumentType = includedTypes.Count == 2 ? "Both" : includedTypes[0].Type;
                result = await _userProfileService.ApplyBulkInitialTrainingAsync(dto, includedTypes[0].RestrictToAssignedToId);
            }
            else
            {
                // Mixed authorization (e.g. officer on one type, line manager on the other) — one
                // call per type, merged.
                result = new BulkInitialTrainingResultDTO();
                foreach (var (type, restrictToAssignedToId) in includedTypes)
                {
                    var typeDto = new BulkInitialTrainingDTO
                    {
                        DocumentType = type,
                        IntroductoryTrainingDate = dto.IntroductoryTrainingDate,
                        IntroductoryTrainingHours = dto.IntroductoryTrainingHours,
                        IntroductoryTrainingInstructor = dto.IntroductoryTrainingInstructor,
                        IntroductoryTrainingInstructorFunction = dto.IntroductoryTrainingInstructorFunction,
                        IntroductoryTrainingContent = dto.IntroductoryTrainingContent,
                        WorkplaceTrainingDate = dto.WorkplaceTrainingDate,
                        WorkplaceTrainingLocation = dto.WorkplaceTrainingLocation,
                        WorkplaceTrainingHours = dto.WorkplaceTrainingHours,
                        WorkplaceTrainingInstructor = dto.WorkplaceTrainingInstructor,
                        WorkplaceTrainingInstructorFunction = dto.WorkplaceTrainingInstructorFunction,
                        WorkplaceTrainingContent = dto.WorkplaceTrainingContent,
                        SelectedDepartmentId = dto.SelectedDepartmentId,
                        ApplyToAllUsers = dto.ApplyToAllUsers,
                        SelectedUserIds = dto.SelectedUserIds
                    };
                    var typeResult = await _userProfileService.ApplyBulkInitialTrainingAsync(typeDto, restrictToAssignedToId);
                    result.SuccessCount += typeResult.SuccessCount;
                    result.SkippedCount += typeResult.SkippedCount;
                    result.FailedCount += typeResult.FailedCount;
                    result.Errors.AddRange(typeResult.Errors);
                }
            }

            return result.NoUsersMatched ? BadRequest(result) : Ok(result);
        }
    }
}
