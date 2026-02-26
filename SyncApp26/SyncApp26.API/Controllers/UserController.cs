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

        public UserController(IUserService userService, IDepartmentService departmentService)
        {
            _userService = userService;
            _departmentService = departmentService;
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
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
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
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserGETResponseDTO>>> GetAllUsers()
        {
            var users = await _userService.GetAllUsersAsync();
            var responseList = users.Select(user => new UserGETResponseDTO
            {
                Id = user.Id,
                PersonalId = user.PersonalId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
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
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = user.AssignedTo != null ? $"{user.AssignedTo.FirstName} {user.AssignedTo.LastName}" : null,
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
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                DepartmentName = user.Department?.Name ?? "Unknown",
                AssignedToId = user.AssignedToId,
                AssignedToName = $"{lineManager.FirstName} {lineManager.LastName}",
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

            var user = new User
            {
                Id = Guid.NewGuid(),
                PersonalId = Guid.NewGuid().ToString(),
                FirstName = userRequestDTO.FirstName,
                LastName = userRequestDTO.LastName,
                Email = userRequestDTO.Email,
                DepartmentId = userRequestDTO.DepartmentId,
                AssignedToId = userRequestDTO.AssignedToId,
                CreatedAt = DateTime.UtcNow
            };

            await _userService.AddUserAsync(user);

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

            existingUser.FirstName = userRequestDTO.FirstName;
            existingUser.LastName = userRequestDTO.LastName;
            existingUser.Email = userRequestDTO.Email;
            existingUser.DepartmentId = userRequestDTO.DepartmentId;
            existingUser.AssignedToId = userRequestDTO.AssignedToId;
            existingUser.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(existingUser);

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
    }
}
