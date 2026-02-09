using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.CSV.History;
using SyncApp26.Domain.Entities;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportConflictController : ControllerBase
    {
        private IImportConflictService _importConflictService;

        public ImportConflictController(IImportConflictService importConflictService)
        {
            _importConflictService = importConflictService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllImportConflicts()
        {
            var conflicts = await _importConflictService.GetAllImportConflictsAsync();
            return Ok(conflicts);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetImportConflictById(Guid id)
        {
            var conflict = await _importConflictService.GetImportConflictByIdAsync(id);
            if (conflict == null)
            {
                return NotFound();
            }
            return Ok(conflict);
        }

        [HttpGet("byImportHistory/{importHistoryId}")]
        public async Task<IActionResult> GetImportConflictsByImportHistoryId(Guid importHistoryId)
        {
            var conflicts = await _importConflictService.GetImportConflictsByImportHistoryIdAsync(importHistoryId);
            return Ok(conflicts);
        }

        [HttpGet("byUser/{userId}")]
        public async Task<IActionResult> GetImportConflictsByUserId(Guid userId)
        {
            var conflicts = await _importConflictService.GetImportConflictsByUserIdAsync(userId);
            return Ok(conflicts);
        }

        [HttpPost]
        public async Task<IActionResult> CreateImportConflict([FromBody] ImportConflictRequestDTO importConflictRequestDTO)
        {
            if(importConflictRequestDTO == null || importConflictRequestDTO.UserId == Guid.Empty || importConflictRequestDTO.ImportHistoryId == Guid.Empty || string.IsNullOrEmpty(importConflictRequestDTO.FieldName))
            {
                return BadRequest("Invalid import conflict data.");
            }

            var importConflict = new ImportConflict
            {
                Id = Guid.NewGuid(),
                ImportHistoryId = importConflictRequestDTO.ImportHistoryId,
                UserId = importConflictRequestDTO.UserId,
                FieldName = importConflictRequestDTO.FieldName,
                OldValue = importConflictRequestDTO.OldValue,
                NewValue = importConflictRequestDTO.NewValue,
                Status = importConflictRequestDTO.Status
            };

            await _importConflictService.AddImportConflictAsync(importConflict);
            return CreatedAtAction(nameof(GetImportConflictById), new { id = importConflict.Id }, importConflict);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteImportConflict(Guid id)
        {
            await _importConflictService.DeleteImportConflictAsync(id);
            return NoContent();
        }
    }
}