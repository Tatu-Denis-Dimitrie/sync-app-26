using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Shared.DTOs.CSV.History;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportHistoryController : ControllerBase
    {
        private readonly IImportHistoryService _importHistoryService;

        public ImportHistoryController(IImportHistoryService importHistoryService)
        {
            _importHistoryService = importHistoryService;
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
        public async Task<IActionResult> AddImportHistory([FromBody] ImportHistoryRequestDTO importHistoryRequestDTO)
        {
            if(importHistoryRequestDTO == null || string.IsNullOrEmpty(importHistoryRequestDTO.FileName))
            {
                return BadRequest("Invalid import history data.");
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
        public async Task<IActionResult> DeleteImportHistory(Guid id)
        {
            await _importHistoryService.DeleteImportHistoryAsync(id);
            return NoContent();
        }
    }
}