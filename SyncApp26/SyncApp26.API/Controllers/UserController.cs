using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Shared.DTOs.Response.User;
using System.ComponentModel.DataAnnotations;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IDepartmentService _departmentService;
        private readonly IFunctionService _functionService;
        private readonly IUserChangeHistoryService _userChangeHistoryService;
        private readonly IDocumentService _documentService;
        private readonly IPeriodicTrainingService _periodicTrainingService;

        public UserController(IUserService userService, IDepartmentService departmentService, IFunctionService functionService, IUserChangeHistoryService userChangeHistoryService, IDocumentService documentService, IPeriodicTrainingService periodicTrainingService)
        {
            _userService = userService;
            _departmentService = departmentService;
            _functionService = functionService;
            _userChangeHistoryService = userChangeHistoryService;
            _documentService = documentService;
            _periodicTrainingService = periodicTrainingService;
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
                RoleId = user.RoleId,
                RoleName = user.Role?.Name,
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
                RoleId = user.RoleId,
                RoleName = user.Role?.Name,
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
            var users = await _userService.GetAllUsersAsync();
            var ssmIds = await _documentService.GetUserIdsWithDocumentTypeAsync("SSM");
            var suIds = await _documentService.GetUserIdsWithDocumentTypeAsync("SU");
            var unsignedSsmIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync("SSM");
            var unsignedSuIds = await _documentService.GetUserIdsWithUnsignedDocumentTypeAsync("SU");

            var responseList = users.Select(user => new UserGETResponseDTO
            {
                Id = user.Id,
                PersonalId = user.PersonalId,
                RoleId = user.RoleId,
                RoleName = user.Role?.Name,
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
                RoleId = user.RoleId,
                RoleName = user.Role?.Name,
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
                RoleId = user.RoleId,
                RoleName = user.Role?.Name,
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
            if (string.IsNullOrEmpty(userRequestDTO.FirstName) ||
                string.IsNullOrEmpty(userRequestDTO.LastName) ||
                string.IsNullOrEmpty(userRequestDTO.Email))
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "FirstName, LastName, and Email are required"
                });
            }

            // Validate email format
            if (!new EmailAddressAttribute().IsValid(userRequestDTO.Email))
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "Invalid email format"
                });
            }

            // Verify department exists
            var department = await _departmentService.GetDepartmentByIdAsync(userRequestDTO.DepartmentId);
            if (department == null)
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "Department not found"
                });
            }

            // Verify assigned to user exists if provided
            if (userRequestDTO.AssignedToId != null)
            {
                var assignedTo = await _userService.GetUserByIdAsync(userRequestDTO.AssignedToId.Value);
                if (assignedTo == null)
                {
                    return BadRequest(new UserResponseDTO
                    {
                        Success = false,
                        Message = "Assigned to user not found"
                    });
                }
            }

            var roleId = await _userService.GetRoleIdByNameAsync("Basic User");
            if (roleId == null)
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "Role 'Basic User' not found"
                });
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                PersonalId = Guid.NewGuid().ToString(),
                RoleId = roleId.Value,
                FirstName = userRequestDTO.FirstName,
                LastName = userRequestDTO.LastName,
                Email = userRequestDTO.Email,
                DepartmentId = userRequestDTO.DepartmentId,
                AssignedToId = userRequestDTO.AssignedToId,
                FunctionId = await ResolveFunctionIdAsync(userRequestDTO.Function),
                CreatedAt = DateTime.UtcNow
            };

            await _userService.AddUserAsync(user);

            // If this new user is assigned to a manager, promote that manager to Line Manager
            if (userRequestDTO.AssignedToId.HasValue)
            {
                var managerToPromote = await _userService.GetUserByIdAsync(userRequestDTO.AssignedToId.Value);
                if (managerToPromote != null)
                {
                    var lineManagerRoleId = await _userService.GetRoleIdByNameAsync("Line Manager");
                    if (lineManagerRoleId.HasValue && managerToPromote.RoleId != lineManagerRoleId.Value)
                    {
                        managerToPromote.RoleId = lineManagerRoleId.Value;
                        await _userService.UpdateUserAsync(managerToPromote);
                    }
                }
            }

            return Ok(new UserResponseDTO
            {
                Success = true,
                Message = "User created successfully"
            });
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

            if (string.IsNullOrEmpty(userRequestDTO.FirstName) ||
                string.IsNullOrEmpty(userRequestDTO.LastName) ||
                string.IsNullOrEmpty(userRequestDTO.Email))
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "FirstName, LastName, and Email are required"
                });
            }

            // Validate email format
            if (!new EmailAddressAttribute().IsValid(userRequestDTO.Email))
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "Invalid email format"
                });
            }

            // Check for circular assignment (user cannot be assigned to themselves)
            if (userRequestDTO.AssignedToId != null && userRequestDTO.AssignedToId == existingUser.Id)
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "User cannot be assigned to themselves"
                });
            }

            // Verify department exists
            var department = await _departmentService.GetDepartmentByIdAsync(userRequestDTO.DepartmentId);
            if (department == null)
            {
                return BadRequest(new UserResponseDTO
                {
                    Success = false,
                    Message = "Department not found"
                });
            }

            var oldFirstName = existingUser.FirstName;
            var oldLastName = existingUser.LastName;
            var oldEmail = existingUser.Email;
            var oldDepartmentId = existingUser.DepartmentId;
            var oldDepartmentName = existingUser.Department?.Name ?? string.Empty;
            var oldAssignedToId = existingUser.AssignedToId;
            var oldAssignedToName = existingUser.AssignedTo != null
                ? $"{existingUser.AssignedTo.FirstName} {existingUser.AssignedTo.LastName}".Trim()
                : string.Empty;
            var oldFunctionId = existingUser.FunctionId;
            var oldFunctionName = existingUser.Function?.Name ?? "Unknown";

            var newDepartmentName = department.Name;
            User? assignedToUser = null;

            // Verify assigned to user exists if provided
            if (userRequestDTO.AssignedToId != null)
            {
                var assignedTo = await _userService.GetUserByIdAsync(userRequestDTO.AssignedToId.Value);
                if (assignedTo == null)
                {
                    return BadRequest(new UserResponseDTO
                    {
                        Success = false,
                        Message = "Assigned to user not found"
                    });
                }

                assignedToUser = assignedTo;

                // Check for circular reference: ensure the assignedTo user is not already managed by this user
                if (assignedTo.AssignedToId == existingUser.Id)
                {
                    return BadRequest(new UserResponseDTO
                    {
                        Success = false,
                        Message = "Circular assignment detected: Cannot assign a user to someone who reports to them"
                    });
                }

            }

            var resolvedFunctionId = await ResolveFunctionIdAsync(userRequestDTO.Function);
            var newFunctionName = string.IsNullOrWhiteSpace(userRequestDTO.Function)
                ? "Unknown"
                : userRequestDTO.Function.Trim();
            var newAssignedToName = assignedToUser != null
                ? $"{assignedToUser.FirstName} {assignedToUser.LastName}".Trim()
                : string.Empty;

            existingUser.FirstName = userRequestDTO.FirstName;
            existingUser.LastName = userRequestDTO.LastName;
            existingUser.Email = userRequestDTO.Email;
            existingUser.DepartmentId = userRequestDTO.DepartmentId;
            existingUser.AssignedToId = userRequestDTO.AssignedToId;
            existingUser.FunctionId = resolvedFunctionId;
            existingUser.UpdatedAt = DateTime.UtcNow;

            // Apply role from DTO if provided
            if (!string.IsNullOrWhiteSpace(userRequestDTO.RoleName))
            {
                var requestedRoleId = await _userService.GetRoleIdByNameAsync(userRequestDTO.RoleName);
                if (requestedRoleId.HasValue)
                    existingUser.RoleId = requestedRoleId.Value;
            }

            await _userService.UpdateUserAsync(existingUser);

            var now = DateTime.UtcNow;
            var manualChanges = new List<UserChangeHistory>();

            if (!string.Equals(oldFirstName, existingUser.FirstName, StringComparison.Ordinal))
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "FirstName",
                    OldValue = oldFirstName,
                    NewValue = existingUser.FirstName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (!string.Equals(oldLastName, existingUser.LastName, StringComparison.Ordinal))
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "LastName",
                    OldValue = oldLastName,
                    NewValue = existingUser.LastName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (!string.Equals(oldEmail, existingUser.Email, StringComparison.Ordinal))
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "Email",
                    OldValue = oldEmail,
                    NewValue = existingUser.Email,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (oldDepartmentId != existingUser.DepartmentId)
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "DepartmentName",
                    OldValue = oldDepartmentName,
                    NewValue = newDepartmentName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (oldAssignedToId != existingUser.AssignedToId)
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "AssignedToName",
                    OldValue = oldAssignedToName,
                    NewValue = newAssignedToName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            if (oldFunctionId != existingUser.FunctionId)
            {
                manualChanges.Add(new UserChangeHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = existingUser.Id,
                    FieldName = "FunctionName",
                    OldValue = oldFunctionName,
                    NewValue = newFunctionName,
                    ImportHistoryId = null,
                    Status = null,
                    CreatedAt = now
                });
            }

            foreach (var change in manualChanges)
            {
                await _userChangeHistoryService.AddUserChangeHistoryAsync(change);
            }

            return Ok(new UserResponseDTO
            {
                Success = true,
                Message = "User updated successfully"
            });
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
                RoleName = user.Role?.Name,
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
                IntroductoryTrainingDate = user.IntroductoryTrainingDate,
                IntroductoryTrainingHours = user.IntroductoryTrainingHours,
                IntroductoryTrainingInstructor = user.IntroductoryTrainingInstructor,
                IntroductoryTrainingInstructorFunction = user.IntroductoryTrainingInstructorFunction,
                IntroductoryTrainingContent = user.IntroductoryTrainingContent,
                WorkplaceTrainingDate = user.WorkplaceTrainingDate,
                WorkplaceTrainingLocation = user.WorkplaceTrainingLocation,
                WorkplaceTrainingHours = user.WorkplaceTrainingHours,
                WorkplaceTrainingInstructor = user.WorkplaceTrainingInstructor,
                WorkplaceTrainingInstructorFunction = user.WorkplaceTrainingInstructorFunction,
                WorkplaceTrainingContent = user.WorkplaceTrainingContent,
                AdmittedByName = user.AdmittedByName,
                AdmittedByFunction = user.AdmittedByFunction,
                AdmittedDate = user.AdmittedDate,
                HireDate = user.CreatedAt, // Using CreatedAt as HireDate
                CreatedAt = user.CreatedAt
                ,
                // include latest signatures when available
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

            // Update SSM/SU fields
            user.DateOfBirth = dto.DateOfBirth;
            user.PlaceOfBirth = dto.PlaceOfBirth;
            user.Address = dto.Address;
            user.BloodGroup = dto.BloodGroup;
            user.BadgeNumber = dto.BadgeNumber;
            user.Education = dto.Education;
            user.Qualifications = dto.Qualifications;
            user.CommuteRoute = dto.CommuteRoute;
            user.CommuteDurationMinutes = dto.CommuteDurationMinutes;

            // Update training fields
            user.IntroductoryTrainingDate = dto.IntroductoryTrainingDate;
            user.IntroductoryTrainingHours = dto.IntroductoryTrainingHours;
            user.IntroductoryTrainingInstructor = dto.IntroductoryTrainingInstructor;
            user.IntroductoryTrainingInstructorFunction = dto.IntroductoryTrainingInstructorFunction;
            user.IntroductoryTrainingContent = dto.IntroductoryTrainingContent;

            user.WorkplaceTrainingDate = dto.WorkplaceTrainingDate;
            user.WorkplaceTrainingLocation = dto.WorkplaceTrainingLocation;
            user.WorkplaceTrainingHours = dto.WorkplaceTrainingHours;
            user.WorkplaceTrainingInstructor = dto.WorkplaceTrainingInstructor;
            user.WorkplaceTrainingInstructorFunction = dto.WorkplaceTrainingInstructorFunction;
            user.WorkplaceTrainingContent = dto.WorkplaceTrainingContent;

            user.AdmittedByName = dto.AdmittedByName;
            user.AdmittedByFunction = dto.AdmittedByFunction;
            user.AdmittedDate = dto.AdmittedDate;

            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return Ok(new UserResponseDTO
            {
                Success = true,
                Message = "SSM/SU form updated successfully"
            });
        }

        [HttpPost("bulk-initial-training")]
        public async Task<ActionResult<BulkInitialTrainingResultDTO>> BulkInitialTraining([FromBody] BulkInitialTrainingDTO dto)
        {
            var result = new BulkInitialTrainingResultDTO();

            var users = await _userService.GetAllUsersAsync();
            var targetUsers = users.AsQueryable();

            if (dto.SelectedDepartmentId.HasValue)
            {
                targetUsers = targetUsers.Where(u => u.DepartmentId == dto.SelectedDepartmentId.Value);
            }

            if (!dto.ApplyToAllUsers)
            {
                var selected = dto.SelectedUserIds?.ToHashSet() ?? new HashSet<Guid>();
                targetUsers = targetUsers.Where(u => selected.Contains(u.Id));
            }

            var usersToUpdate = targetUsers.ToList();
            if (!usersToUpdate.Any())
            {
                result.Errors.Add("No users found to apply initial training data.");
                return BadRequest(result);
            }

            foreach (var user in usersToUpdate)
            {
                try
                {
                    var changed = false;

                    if (!user.IntroductoryTrainingDate.HasValue && dto.IntroductoryTrainingDate.HasValue)
                    {
                        user.IntroductoryTrainingDate = dto.IntroductoryTrainingDate;
                        changed = true;
                    }

                    if (!user.IntroductoryTrainingHours.HasValue && dto.IntroductoryTrainingHours.HasValue)
                    {
                        user.IntroductoryTrainingHours = dto.IntroductoryTrainingHours;
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(user.IntroductoryTrainingInstructor) && !string.IsNullOrWhiteSpace(dto.IntroductoryTrainingInstructor))
                    {
                        user.IntroductoryTrainingInstructor = dto.IntroductoryTrainingInstructor.Trim();
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(user.IntroductoryTrainingInstructorFunction) && !string.IsNullOrWhiteSpace(dto.IntroductoryTrainingInstructorFunction))
                    {
                        user.IntroductoryTrainingInstructorFunction = dto.IntroductoryTrainingInstructorFunction.Trim();
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(user.IntroductoryTrainingContent) && !string.IsNullOrWhiteSpace(dto.IntroductoryTrainingContent))
                    {
                        user.IntroductoryTrainingContent = dto.IntroductoryTrainingContent.Trim();
                        changed = true;
                    }

                    if (!user.WorkplaceTrainingDate.HasValue && dto.WorkplaceTrainingDate.HasValue)
                    {
                        user.WorkplaceTrainingDate = dto.WorkplaceTrainingDate;
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(user.WorkplaceTrainingLocation) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingLocation))
                    {
                        user.WorkplaceTrainingLocation = dto.WorkplaceTrainingLocation.Trim();
                        changed = true;
                    }

                    if (!user.WorkplaceTrainingHours.HasValue && dto.WorkplaceTrainingHours.HasValue)
                    {
                        user.WorkplaceTrainingHours = dto.WorkplaceTrainingHours;
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(user.WorkplaceTrainingInstructor) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingInstructor))
                    {
                        user.WorkplaceTrainingInstructor = dto.WorkplaceTrainingInstructor.Trim();
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(user.WorkplaceTrainingInstructorFunction) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingInstructorFunction))
                    {
                        user.WorkplaceTrainingInstructorFunction = dto.WorkplaceTrainingInstructorFunction.Trim();
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(user.WorkplaceTrainingContent) && !string.IsNullOrWhiteSpace(dto.WorkplaceTrainingContent))
                    {
                        user.WorkplaceTrainingContent = dto.WorkplaceTrainingContent.Trim();
                        changed = true;
                    }

                    if (!changed)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    user.UpdatedAt = DateTime.UtcNow;
                    await _userService.UpdateUserAsync(user);
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add($"Failed for user {user.Email}: {ex.Message}");
                }
            }

            return Ok(result);
        }
        private async Task<Guid?> ResolveFunctionIdAsync(string? requestedFunction)
        {
            if (!string.IsNullOrWhiteSpace(requestedFunction))
            {
                var existingFunction = await _functionService.GetByNameAsync(requestedFunction);
                if (existingFunction != null)
                {
                    return existingFunction.Id;
                }
            }

            var unknownFunction = await _functionService.GetByNameAsync("Unknown");
            return unknownFunction?.Id;
        }
    }
}
