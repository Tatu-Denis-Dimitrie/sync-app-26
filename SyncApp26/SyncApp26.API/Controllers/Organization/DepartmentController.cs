using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.Department;
using SyncApp26.Shared.DTOs.Response.Department;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DepartmentController : ControllerBase
    {
        private readonly IDepartmentService _departmentService;
        private readonly IStringLocalizer _localizer;

        public DepartmentController(IDepartmentService departmentService, ILocalizationService localizationService)
        {
            _departmentService = departmentService;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Organization);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<DepartmentGETResponseDTO>> GetDepartmentById(Guid id)
        {
            var department = await _departmentService.GetDepartmentByIdAsync(id);
            if (department == null)
            {
                return NotFound();
            }
            return Ok(new DepartmentGETResponseDTO
            {
                Id = department.Id,
                Name = department.Name,
                IsActive = department.IsActive
            });
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DepartmentGETResponseDTO>>> GetAllDepartments()
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
            return Ok(departments.Select(d => new DepartmentGETResponseDTO
            {
                Id = d.Id,
                Name = d.Name,
                IsActive = d.IsActive
            }));
        }

        [HttpGet("scheduled-for-deletion")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<IEnumerable<DepartmentGETResponseDTO>>> GetScheduledForDeletionDepartments()
        {
            var departments = await _departmentService.GetDeletedDepartmentsAsync();
            return Ok(departments.Select(d => new DepartmentGETResponseDTO
            {
                Id = d.Id,
                Name = d.Name,
                IsActive = d.IsActive,
                DeletedAt = d.DeletedAt
            }));
        }

        [HttpPost("{id}/restore")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<DepartmentResponseDTO>> RestoreDepartment(Guid id)
        {
            var existingDepartment = await _departmentService.GetDeletedDepartmentByIdAsync(id);
            if (existingDepartment == null)
            {
                return new DepartmentResponseDTO
                {
                    Success = false,
                    Message = _localizer["department.scheduledForDeletionNotFound"].Value,
                };
            }

            existingDepartment.DeletedAt = null;
            existingDepartment.IsActive = false; // Restore as inactive by default
            existingDepartment.UpdatedAt = DateTime.UtcNow;

            await _departmentService.UpdateDepartmentAsync(existingDepartment);

            return new DepartmentResponseDTO
            {
                Success = true,
                Message = _localizer["department.restored"].Value
            };
        }

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<DepartmentResponseDTO>> AddDepartment([FromBody] DepartmentRequestDTO departmentRequestDTO)
        {
            var department = new Department
            {
                Id = Guid.NewGuid(),
                Name = departmentRequestDTO.Name.Trim(),
                IsActive = departmentRequestDTO.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await _departmentService.AddDepartmentAsync(department);

            return new DepartmentResponseDTO
            {
                Success = true,
                Message = _localizer["department.created"].Value,
            };
        }

        [HttpPut("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<DepartmentResponseDTO>> UpdateDepartment(Guid id, [FromBody] DepartmentRequestDTO departmentRequestDTO)
        {
            if (string.IsNullOrEmpty(departmentRequestDTO.Name))
            {
                return new DepartmentResponseDTO
                {
                    Success = false,
                    Message = _localizer["department.nameRequired"].Value,
                };
            }

            var department = new Department
            {
                Id = id,
                Name = departmentRequestDTO.Name.Trim(),
                IsActive = departmentRequestDTO.IsActive,
                UpdatedAt = DateTime.UtcNow
            };

            await _departmentService.UpdateDepartmentAsync(department);
            return new DepartmentResponseDTO
            {
                Success = true,
                Message = _localizer["department.updated"].Value,
            };
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<DepartmentResponseDTO>> DeleteDepartment(Guid id, [FromQuery] Guid? transferToId, [FromServices] IUserService userService)
        {
            var existingDepartment = await _departmentService.GetDepartmentByIdAsync(id);
            if (existingDepartment == null)
            {
                return new DepartmentResponseDTO
                {
                    Success = false,
                    Message = _localizer["department.notFound"].Value,
                };
            }

            var usersInDepartment = await userService.GetUsersByDepartmentIdAsync(id);
            if (usersInDepartment.Any())
            {
                if (!transferToId.HasValue)
                {
                    return BadRequest(new DepartmentResponseDTO
                    {
                        Success = false,
                        Message = _localizer["department.hasAssignedUsers"].Value
                    });
                }

                var transferDepartment = await _departmentService.GetDepartmentByIdAsync(transferToId.Value);
                if (transferDepartment == null)
                {
                    return BadRequest(new DepartmentResponseDTO
                    {
                        Success = false,
                        Message = _localizer["department.transferNotFound"].Value
                    });
                }

                if (transferToId.Value == id)
                {
                    return BadRequest(new DepartmentResponseDTO
                    {
                        Success = false,
                        Message = _localizer["department.cannotTransferToSame"].Value
                    });
                }

                foreach (var user in usersInDepartment)
                {
                    user.DepartmentId = transferToId.Value;
                    await userService.UpdateUserAsync(user);
                }
            }

            existingDepartment.IsActive = false;
            existingDepartment.DeletedAt = DateTime.UtcNow;
            await _departmentService.UpdateDepartmentAsync(existingDepartment);
            
            return new DepartmentResponseDTO
            {
                Success = true,
                Message = _localizer["department.deleted"].Value,
            };
        }
    }
}