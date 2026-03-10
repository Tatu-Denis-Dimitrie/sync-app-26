using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.PeriodicTraining;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PeriodicTrainingController : ControllerBase
    {
        private readonly IPeriodicTrainingService _periodicTrainingService;

        public PeriodicTrainingController(IPeriodicTrainingService periodicTrainingService)
        {
            _periodicTrainingService = periodicTrainingService;
        }

        /// <summary>
        /// Create a new periodic training record for a user
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePeriodicTrainingDTO dto)
        {
            try
            {
                var result = await _periodicTrainingService.CreateAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Get a periodic training record by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _periodicTrainingService.GetByIdAsync(id);
            if (result == null)
                return NotFound(new { message = "Periodic training not found" });

            return Ok(result);
        }

        /// <summary>
        /// Get all periodic training records for a user
        /// </summary>
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var result = await _periodicTrainingService.GetByUserIdAsync(userId);
            return Ok(result);
        }

        /// <summary>
        /// Update a periodic training record
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePeriodicTrainingDTO dto)
        {
            try
            {
                var result = await _periodicTrainingService.UpdateAsync(id, dto);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Delete a periodic training record
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var success = await _periodicTrainingService.DeleteAsync(id);
            if (!success)
                return NotFound(new { message = "Periodic training not found" });

            return Ok(new { message = "Periodic training deleted successfully" });
        }

        /// <summary>
        /// Create periodic training records for multiple users at once
        /// </summary>
        [HttpPost("bulk")]
        public async Task<IActionResult> BulkCreate([FromBody] BulkCreatePeriodicTrainingDTO dto)
        {
            try
            {
                var result = await _periodicTrainingService.BulkCreateAsync(dto);

                if (result.FailedCount > 0 && result.SuccessCount == 0)
                {
                    return BadRequest(new
                    {
                        message = "All bulk creations failed",
                        errors = result.Errors,
                        result
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
