using Microsoft.AspNetCore.Mvc;
using SyncApp26.Shared.DTOs;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;

namespace SyncApp26.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CsvSyncController : ControllerBase
{
    private readonly ICsvSyncService _csvSyncService;
    private readonly ICsvValidationService _csvValidationService;
    private readonly ILogger<CsvSyncController> _logger;

    public CsvSyncController(
        ICsvSyncService csvSyncService,
        ICsvValidationService csvValidationService,
        ILogger<CsvSyncController> logger)
    {
        _csvSyncService = csvSyncService;
        _csvValidationService = csvValidationService;
        _logger = logger;
    }
    /// <summary>
    /// Upload CSV file and compare with database
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB limit for large CSVs
    public async Task<ActionResult<List<UserComparisonDTO>>> UploadAndCompare(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded" });
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "File must be a CSV file" });
        }

        // Get connection ID for SignalR progress updates
        string? connectionId = Request.Headers["X-Connection-Id"].FirstOrDefault() ?? Request.Query["connectionId"].FirstOrDefault();

        try
        {
            int totalRows = 0;

            // Validate CSV file first
            using (var validationStream = file.OpenReadStream())
            {
                var validationResult = await _csvValidationService.ValidateCsvFile(validationStream, file.FileName);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning($"CSV validation failed: {validationResult.Errors.Count} errors");
                    return BadRequest(new
                    {
                        error = "CSV validation failed",
                        errors = validationResult.Errors,
                        warnings = validationResult.Warnings,
                        totalRows = validationResult.TotalRows,
                        validRows = validationResult.ValidRows,
                        invalidRows = validationResult.InvalidRows
                    });
                }

                totalRows = validationResult.TotalRows;

                if (validationResult.Warnings.Count > 0)
                {
                    _logger.LogInformation($"CSV validation passed with {validationResult.Warnings.Count} warnings");
                }
            }

            // Re-open stream for processing
            using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = null
            }))
            {
                csv.Context.RegisterClassMap<CsvUserMap>();
                
                // Use Enumerable to stream records instead of loading all into memory
                var csvUsers = csv.GetRecords<CsvUserDTO>();
                
                // Pass connectionId and totalRows for progress tracking
                var comparisons = await _csvSyncService.CompareWithDatabase(csvUsers, totalRows, connectionId);
                
                _logger.LogInformation($"Compared CSV with {totalRows} rows, found {comparisons.Count} comparisons");

                return Ok(comparisons);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing CSV file");
            return StatusCode(500, new { error = $"Error processing CSV: {ex.Message}" });
        }
    }

    /// <summary>
    /// Sync selected users with resolved conflicts
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<SyncResultDTO>> SyncUsers([FromBody] SyncRequestDTO syncRequest)
    {
        if (syncRequest?.Items == null || syncRequest.Items.Count == 0)
        {
            return BadRequest(new { error = "No sync items provided" });
        }

        // Get connection ID for SignalR progress updates
        string? connectionId = Request.Headers["X-Connection-Id"].FirstOrDefault() ?? Request.Query["connectionId"].FirstOrDefault();

        try
        {
            var result = await _csvSyncService.SyncUsers(syncRequest, connectionId);
            _logger.LogInformation($"Sync completed: {result.RecordsProcessed} processed, {result.RecordsFailed} failed");

            if (!result.Success)
            {
                return StatusCode(207, result); // 207 Multi-Status for partial success
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing users");
            return StatusCode(500, new { error = $"Error syncing users: {ex.Message}" });
        }
    }
}

/// <summary>
/// CSV mapping configuration for CsvHelper
/// </summary>
public sealed class CsvUserMap : ClassMap<CsvUserDTO>
{
    public CsvUserMap()
    {
        Map(m => m.FirstName).Name("FirstName", "First Name", "first_name");
        Map(m => m.LastName).Name("LastName", "Last Name", "last_name");
        Map(m => m.Email).Name("Email", "email");
        Map(m => m.DepartmentName).Name("DepartmentName", "Department Name", "Department", "department_name", "department");
        Map(m => m.AssignedToEmail).Name("AssignedToEmail", "Assigned To Email", "Line Manager Email", "Manager Email", "assigned_to_email", "manager_email").Optional();
    }
}
