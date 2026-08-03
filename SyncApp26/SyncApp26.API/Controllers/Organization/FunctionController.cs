using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.Organization;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
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

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> AddFunction([FromBody] FunctionRequestDTO request)
        {
            await _functionService.AddFunctionAsync(request.Name);
            return Ok();
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> DeleteFunction(Guid id)
        {
            await _functionService.DeleteFunctionAsync(id);
            return Ok();
        }
    }
}