using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.CSV.History;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = Roles.Admin)]
    public class ImportHistoryController : ControllerBase
    {
        private readonly IImportHistoryService _importHistoryService;
        private readonly IStringLocalizer _localizer;

        public ImportHistoryController(IImportHistoryService importHistoryService, ILocalizationService localizationService)
        {
            _importHistoryService = importHistoryService;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Sync);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllImportHistories()
        {
            var histories = await _importHistoryService.GetAllImportHistoriesAsync();
            return Ok(histories);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetImportHistoryById(Guid id)
        {
            var history = await _importHistoryService.GetImportHistoryByIdAsync(id);
            if (history == null)
            {
                return NotFound();
            }
            return Ok(history);
        }

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> AddImportHistory([FromBody] ImportHistoryRequestDTO importHistoryRequestDTO)
        {
            if(importHistoryRequestDTO == null || string.IsNullOrEmpty(importHistoryRequestDTO.FileName))
            {
                return BadRequest(_localizer["csvSync.invalidImportHistoryData"].Value);
            }

            var importHistory = new ImportHistory
            {
                Id = Guid.NewGuid(),
                ImportDate = DateTime.UtcNow,
                FileName = importHistoryRequestDTO.FileName
            };

            await _importHistoryService.AddImportHistoryAsync(importHistory);
            return CreatedAtAction(nameof(GetImportHistoryById), new { id = importHistory.Id }, importHistory);
        }


        [HttpDelete("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> DeleteImportHistory(Guid id)
        {
            await _importHistoryService.DeleteImportHistoryAsync(id);
            return NoContent();
        }
    }
}