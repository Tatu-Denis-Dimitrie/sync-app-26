using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.CSV.History;
using SyncApp26.Domain.Entities;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserChangeHistoryController : ControllerBase
    {
        private IUserChangeHistoryService _userChangeHistoryService;

        public UserChangeHistoryController(IUserChangeHistoryService userChangeHistoryService)
        {
            _userChangeHistoryService = userChangeHistoryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllUserChangeHistories()
        {
            var conflicts = await _userChangeHistoryService.GetAllUserChangeHistoriesAsync();
            return Ok(conflicts);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserChangeHistoryById(Guid id)
        {
            var conflict = await _userChangeHistoryService.GetUserChangeHistoryByIdAsync(id);
            if (conflict == null)
            {
                return NotFound();
            }
            return Ok(conflict);
        }

        [HttpGet("byImportHistory/{importHistoryId}")]
        public async Task<IActionResult> GetUserChangeHistoriesByImportHistoryId(Guid importHistoryId)
        {
            var conflicts = await _userChangeHistoryService.GetUserChangeHistoriesByImportHistoryIdAsync(importHistoryId);
            return Ok(conflicts);
        }

        [HttpGet("byUser/{userId}")]
        public async Task<IActionResult> GetUserChangeHistoriesByUserId(Guid userId)
        {
            var conflicts = await _userChangeHistoryService.GetUserChangeHistoriesByUserIdAsync(userId);
            return Ok(conflicts);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUserChangeHistory([FromBody] UserChangeHistoryRequestDTO userChangeHistoryRequestDTO)
        {
            if(userChangeHistoryRequestDTO == null || userChangeHistoryRequestDTO.UserId == Guid.Empty || string.IsNullOrEmpty(userChangeHistoryRequestDTO.FieldName))
            {
                return BadRequest("Invalid user change history data.");
            }

            var userChangeHistory = new UserChangeHistory
            {
                Id = Guid.NewGuid(),
                ImportHistoryId = userChangeHistoryRequestDTO.ImportHistoryId,
                UserId = userChangeHistoryRequestDTO.UserId,
                FieldName = userChangeHistoryRequestDTO.FieldName,
                OldValue = userChangeHistoryRequestDTO.OldValue,
                NewValue = userChangeHistoryRequestDTO.NewValue,
                Status = userChangeHistoryRequestDTO.Status,
                CreatedAt = DateTime.UtcNow
            };

            await _userChangeHistoryService.AddUserChangeHistoryAsync(userChangeHistory);
            return CreatedAtAction(nameof(GetUserChangeHistoryById), new { id = userChangeHistory.Id }, userChangeHistory);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUserChangeHistory(Guid id)
        {
            await _userChangeHistoryService.DeleteUserChangeHistoryAsync(id);
            return NoContent();
        }
    }
}