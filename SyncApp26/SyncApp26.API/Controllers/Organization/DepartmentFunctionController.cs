using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DepartmentFunctionController : ControllerBase
    {
        private readonly IDepartmentFunctionService _departmentFunctionService;

        public DepartmentFunctionController(IDepartmentFunctionService departmentFunctionService)
        {
            _departmentFunctionService = departmentFunctionService;
        }

        [HttpGet("{departmentId}")]
        public async Task<IActionResult> GetFunctionsByDepartmentId(Guid departmentId)
        {
            var functions = await _departmentFunctionService.GetFunctionsByDepartmentIdAsync(departmentId);
            return Ok(functions);
        }

        [HttpPost("{departmentId}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> AddFunctionToDepartment(Guid departmentId, [FromBody] string functionName)
        {
            await _departmentFunctionService.AddFunctionToDepartmentAsync(departmentId, functionName);
            return NoContent();
        }

        [HttpDelete("{departmentId}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> RemoveFunctionFromDepartment(Guid departmentId, [FromBody] string functionName)
        {
            await _departmentFunctionService.RemoveFunctionFromDepartmentAsync(departmentId, functionName);
            return NoContent();
        }
    }
}