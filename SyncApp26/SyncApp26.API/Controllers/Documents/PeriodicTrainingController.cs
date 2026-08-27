using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.PeriodicTraining;
using SyncApp26.API.Extensions;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PeriodicTrainingController : ControllerBase
    {
        private readonly IPeriodicTrainingService _periodicTrainingService;
        private readonly IUserService _userService;
        private readonly ILogger<PeriodicTrainingController> _logger;
        private readonly IStringLocalizer _localizer;

        public PeriodicTrainingController(
            IPeriodicTrainingService periodicTrainingService,
            IUserService userService,
            ILogger<PeriodicTrainingController> logger,
            ILocalizationService localizationService)
        {
            _periodicTrainingService = periodicTrainingService;
            _userService = userService;
            _logger = logger;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Documents);
        }

        // Same officer-or-line-manager reach BulkCreate already enforces per document type, collapsed
        // to a single check since a single training row carries no document type of its own.
        private async Task<bool> CanWriteTrainingForUserAsync(Guid targetUserId)
        {
            if (User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SsmOfficer) || User.IsInRole(Roles.SuOfficer))
                return true;

            if (!User.IsInRole(Roles.LineManager) || User.GetUserId() is not { } callerId)
                return false;

            var target = await _userService.GetUserByIdAsync(targetUserId);
            return target?.AssignedToId == callerId;
        }

        // Same as above, plus the trainee themselves — an employee may read their own training record.
        private async Task<bool> CanReadTrainingForUserAsync(Guid targetUserId) =>
            User.GetUserId() == targetUserId || await CanWriteTrainingForUserAsync(targetUserId);

        /// <summary>
        /// Create a new periodic training record for a user
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePeriodicTrainingDTO dto)
        {
            if (!await CanWriteTrainingForUserAsync(dto.UserId))
                return Forbid();

            try
            {
                var result = await _periodicTrainingService.CreateAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create periodic training for user {UserId}.", dto.UserId);
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
                return NotFound(new { message = _localizer["periodicTraining.notFound"].Value });

            if (!await CanReadTrainingForUserAsync(result.UserId))
                return Forbid();

            return Ok(result);
        }

        /// <summary>
        /// Get all periodic training records for a user
        /// </summary>
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            if (!await CanReadTrainingForUserAsync(userId))
                return Forbid();

            var result = await _periodicTrainingService.GetByUserIdAsync(userId);
            return Ok(result);
        }

        /// <summary>
        /// Update a periodic training record
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePeriodicTrainingDTO dto)
        {
            var existing = await _periodicTrainingService.GetByIdAsync(id);
            if (existing == null)
                return NotFound(new { message = _localizer["periodicTraining.notFound"].Value });

            if (!await CanWriteTrainingForUserAsync(existing.UserId))
                return Forbid();

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
                _logger.LogError(ex, "Failed to update periodic training {TrainingId}.", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Delete a periodic training record
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _periodicTrainingService.GetByIdAsync(id);
            if (existing == null)
                return NotFound(new { message = _localizer["periodicTraining.notFound"].Value });

            if (!await CanWriteTrainingForUserAsync(existing.UserId))
                return Forbid();

            var success = await _periodicTrainingService.DeleteAsync(id);
            if (!success)
                return NotFound(new { message = _localizer["periodicTraining.notFound"].Value });

            return Ok(new { message = _localizer["api.periodicTrainingDeleted"].Value });
        }

        /// <summary>
        /// Marks (or unmarks) a periodic training row — and its historical copies across
        /// regenerated documents — as excluded from every printed output.
        /// </summary>
        [HttpPatch("{id:guid}/print-exclusion")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> SetPrintExclusion(Guid id, [FromBody] UpdatePrintExclusionDTO dto)
        {
            if (User.GetUserId() is not { } adminId)
                return Unauthorized();

            try
            {
                var result = await _periodicTrainingService.SetPrintExclusionAsync(id, dto.Excluded, adminId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set print exclusion for periodic training {TrainingId}.", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Create periodic training records for multiple users at once
        /// </summary>
        [HttpPost("bulk")]
        public async Task<IActionResult> BulkCreate([FromBody] BulkCreatePeriodicTrainingDTO dto)
        {
            try
            {
                var requestedTypes = (dto.DocumentType ?? "Both").Equals("Both", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "SSM", "SU" }
                    : new[] { dto.DocumentType!.ToUpperInvariant() };

                // Each type is authorized independently: the officer for that type can create for
                // anyone; a line manager (with no officer duty on that type) is restricted to their own
                // direct reports; anyone else is dropped from this request rather than failing it outright.
                bool isLineManager = User.IsInRole(Roles.LineManager);
                var currentUserId = User.GetUserId();
                var includedTypes = new List<(string Type, Guid? RestrictToAssignedToId)>();
                foreach (var type in requestedTypes)
                {
                    if (User.CanInitiateFor(type))
                        includedTypes.Add((type, null));
                    else if (isLineManager && currentUserId is { } managerId)
                        includedTypes.Add((type, managerId));
                }

                if (includedTypes.Count == 0)
                    return Forbid();

                BulkCreateResultDTO result;
                if (includedTypes.Select(t => t.RestrictToAssignedToId).Distinct().Count() == 1)
                {
                    // Every included type shares the same restriction — one call, same as before.
                    dto.DocumentType = includedTypes.Count == 2 ? "Both" : includedTypes[0].Type;
                    result = await _periodicTrainingService.BulkCreateAsync(dto, includedTypes[0].RestrictToAssignedToId);
                }
                else
                {
                    // Mixed authorization (e.g. officer on one type, line manager on the other) —
                    // one call per type, merged.
                    result = new BulkCreateResultDTO();
                    foreach (var (type, restrictToAssignedToId) in includedTypes)
                    {
                        var typeDto = new BulkCreatePeriodicTrainingDTO
                        {
                            TrainingDate = dto.TrainingDate,
                            DurationHours = dto.DurationHours,
                            Occupation = dto.Occupation,
                            MaterialTaught = dto.MaterialTaught,
                            VerifierName = dto.VerifierName,
                            DocumentType = type,
                            SelectedDepartmentId = dto.SelectedDepartmentId,
                            ApplyToAllUsers = dto.ApplyToAllUsers,
                            SelectedUserIds = dto.SelectedUserIds
                        };
                        var typeResult = await _periodicTrainingService.BulkCreateAsync(typeDto, restrictToAssignedToId);
                        result.SuccessCount += typeResult.SuccessCount;
                        result.FailedCount += typeResult.FailedCount;
                        result.Errors.AddRange(typeResult.Errors);
                    }
                }

                if (result.FailedCount > 0 && result.SuccessCount == 0)
                {
                    return BadRequest(new
                    {
                        message = _localizer["api.allBulkCreationsFailed"].Value,
                        errors = result.Errors,
                        result
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk periodic training creation failed.");
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
