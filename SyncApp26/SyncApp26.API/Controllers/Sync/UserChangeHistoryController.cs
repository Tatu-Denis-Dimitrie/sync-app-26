using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.CSV.History;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.API.Extensions;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserChangeHistoryController : ControllerBase
    {
        private IUserChangeHistoryService _userChangeHistoryService;
        private readonly IStringLocalizer _localizer;

        public UserChangeHistoryController(IUserChangeHistoryService userChangeHistoryService, ILocalizationService localizationService)
        {
            _userChangeHistoryService = userChangeHistoryService;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Sync);
        }

        [HttpGet]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> GetAllUserChangeHistories()
        {
            var conflicts = await _userChangeHistoryService.GetAllUserChangeHistoriesAsync();
            return Ok(conflicts);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = Roles.Admin)]
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
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> GetUserChangeHistoriesByImportHistoryId(Guid importHistoryId)
        {
            var conflicts = await _userChangeHistoryService.GetUserChangeHistoriesByImportHistoryIdAsync(importHistoryId);
            return Ok(conflicts);
        }

        [HttpGet("byUser/{userId}")]
        public async Task<ActionResult<UserChangeHistoryPageDTO>> GetUserChangeHistoriesByUserId(Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (!User.IsInRole(Roles.Admin) && User.GetUserId() != userId)
            {
                return Forbid();
            }

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (items, totalCount) = await _userChangeHistoryService.GetUserChangeHistoriesByUserIdPageAsync(userId, page, pageSize);
            return Ok(new UserChangeHistoryPageDTO { Items = items, TotalCount = totalCount });
        }

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> CreateUserChangeHistory([FromBody] UserChangeHistoryRequestDTO userChangeHistoryRequestDTO)
        {
            if(userChangeHistoryRequestDTO == null || userChangeHistoryRequestDTO.UserId == Guid.Empty || string.IsNullOrEmpty(userChangeHistoryRequestDTO.FieldName))
            {
                return BadRequest(_localizer["csvSync.invalidUserChangeHistoryData"].Value);
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
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> DeleteUserChangeHistory(Guid id)
        {
            await _userChangeHistoryService.DeleteUserChangeHistoryAsync(id);
            return NoContent();
        }
    }
}