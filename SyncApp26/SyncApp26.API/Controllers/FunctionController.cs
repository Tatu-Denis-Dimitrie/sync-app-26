using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FunctionController : ControllerBase
    {
        private readonly IFunctionService _functionService;

        public FunctionController(IFunctionService functionService)
        {
            _functionService = functionService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllFunctions()
        {
            var functions = await _functionService.GetAllFunctionNamesAsync();
            return Ok(functions);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetFunctionById(Guid id)
        {
            var functions = await _functionService.GetFunctionByIdAsync(id);
            return Ok(functions);
        }

        [HttpGet("department/{departmentId}")]
        public async Task<IActionResult> GetFunctionsByDepartment(Guid departmentId)
        {
            var functions = await _functionService.GetFunctionByDepartmentIdAsync(departmentId);
            return Ok(functions);
        }

        [HttpPost]
        public async Task<IActionResult> AddFunction([FromBody] string functionName)
        {
            await _functionService.AddFunctionAsync(functionName);
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteFunction(Guid id)
        {
            await _functionService.DeleteFunctionAsync(id);
            return Ok();
        }
    }
}