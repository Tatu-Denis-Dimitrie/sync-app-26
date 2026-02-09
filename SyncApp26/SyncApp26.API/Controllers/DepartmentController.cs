using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Shared.DTOs.Request.Department;
using SyncApp26.Shared.DTOs.Response.Department;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DepartmentController : ControllerBase
    {
        private readonly IDepartmentService _departmentService;

        public DepartmentController(IDepartmentService departmentService)
        {
            _departmentService = departmentService;
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
                Name = department.Name
            });
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DepartmentGETResponseDTO>>> GetAllDepartments()
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
            return Ok(departments.Select(d => new DepartmentGETResponseDTO
            {
                Id = d.Id,
                Name = d.Name
            }));
        }

        [HttpPost]
        public async Task<ActionResult<DepartmentResponseDTO>> AddDepartment([FromBody] DepartmentRequestDTO departmentRequestDTO)
        {
            var department = new Department
            {
                Id = Guid.NewGuid(),
                Name = departmentRequestDTO.Name.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await _departmentService.AddDepartmentAsync(department);

            return new DepartmentResponseDTO
            {
                Success = true,
                Message = "Department created successfully",
            };
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<DepartmentResponseDTO>> UpdateDepartment(Guid id, [FromBody] DepartmentRequestDTO departmentRequestDTO)
        {
            if (string.IsNullOrEmpty(departmentRequestDTO.Name))
            {
                return new DepartmentResponseDTO
                {
                    Success = false,
                    Message = "Department name is required",
                };
            }

            var department = new Department
            {
                Id = id,
                Name = departmentRequestDTO.Name.Trim(),
                UpdatedAt = DateTime.UtcNow
            };

            await _departmentService.UpdateDepartmentAsync(department);
            return new DepartmentResponseDTO
            {
                Success = true,
                Message = "Department updated successfully",
            };
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<DepartmentResponseDTO>> DeleteDepartment(Guid id)
        {
            var existingDepartment = await _departmentService.GetDepartmentByIdAsync(id);
            if (existingDepartment == null)
            {
                return new DepartmentResponseDTO
                {
                    Success = false,
                    Message = "Department not found",
                };
            }

            existingDepartment.DeletedAt = DateTime.UtcNow;
            await _departmentService.UpdateDepartmentAsync(existingDepartment);
            return new DepartmentResponseDTO
            {
                Success = true,
                Message = "Department deleted successfully",
            };
        }
    }
}