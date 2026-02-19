using Microsoft.AspNetCore.Mvc;
using SyncApp26.Shared.DTOs;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Shared.DTOs.CSV.Department;

namespace SyncApp26.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CsvSyncController : ControllerBase
{
    private readonly ICsvSyncService _csvSyncService;
    private readonly ICsvValidationService _csvValidationService;
    private readonly IDepartmentService _departmentService;
    private readonly ILogger<CsvSyncController> _logger;

    public CsvSyncController(
        ICsvSyncService csvSyncService,
        ICsvValidationService csvValidationService,
        IDepartmentService departmentService,
        ILogger<CsvSyncController> logger)
    {
        _csvSyncService = csvSyncService;
        _csvValidationService = csvValidationService;
        _departmentService = departmentService;
        _logger = logger;
    }
    /// <summary>
    /// Upload CSV file and compare with database
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB limit for large CSVs
    public async Task<ActionResult<ComparisonResponseDTO>> UploadAndCompare(IFormFile file, [FromQuery] bool skipInvalidRows = false)
    {
        var startTime = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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
            long validationTimeMs = 0;
            long comparisonTimeMs = 0;

            // Step 1: Validate CSV file
            var validationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            CsvValidationResultDTO validationResult;

            // Fetch existing departments for validation
            var existingDepartments = (await _departmentService.GetAllDepartmentsAsync())
                .Select(d => d.Name.ToLower())
                .ToHashSet();

            using (var validationStream = file.OpenReadStream())
            {
                validationResult = await _csvValidationService.ValidateCsvFile(validationStream, file.FileName, existingDepartments);
            }

            validationStopwatch.Stop();
            validationTimeMs = validationStopwatch.ElapsedMilliseconds;

            // If validation failed with all rows invalid, return error
            if (!validationResult.IsValid && validationResult.ValidRows == 0)
            {
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

            // If validation has errors but also valid rows, and user hasn't chosen to skip
            if (!validationResult.IsValid && validationResult.ValidRows > 0 && !skipInvalidRows)
            {
                return BadRequest(new
                {
                    error = "CSV has some invalid rows",
                    errors = validationResult.Errors,
                    warnings = validationResult.Warnings,
                    totalRows = validationResult.TotalRows,
                    validRows = validationResult.ValidRows,
                    invalidRows = validationResult.InvalidRows,
                    canProceedWithValidRows = true // Signal frontend that partial upload is possible
                });
            }

            // Step 2: Parse and compare with database (skip invalid rows if needed)
            var comparisonStopwatch = System.Diagnostics.Stopwatch.StartNew();
            List<UserComparisonDTO> comparisons;
            var invalidRowSet = new HashSet<int>(validationResult.InvalidRowNumbers);

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
                csv.Read();
                csv.ReadHeader();

                // Stream records for processing, skip invalid rows if needed
                var allCsvUsers = csv.GetRecords<CsvUserDTO>().ToList();

                List<CsvUserDTO> csvUsersToProcess;
                if (invalidRowSet.Count > 0 && skipInvalidRows)
                {
                    // Skip invalid rows (row numbers are 1-based, index is 0-based)
                    csvUsersToProcess = allCsvUsers
                        .Select((user, index) => new { user, rowNumber = index + 1 })
                        .Where(x => !invalidRowSet.Contains(x.rowNumber))
                        .Select(x => x.user)
                        .ToList();
                }
                else
                {
                    csvUsersToProcess = allCsvUsers;
                }

                // Pass connectionId for progress tracking
                comparisons = await _csvSyncService.CompareWithDatabase(csvUsersToProcess, csvUsersToProcess.Count, connectionId);
                totalRows = validationResult.TotalRows;
            }

            comparisonStopwatch.Stop();
            comparisonTimeMs = comparisonStopwatch.ElapsedMilliseconds;
            stopwatch.Stop();

            _logger.LogInformation($"Compared CSV with {totalRows} rows, found {comparisons.Count} comparisons in {stopwatch.ElapsedMilliseconds}ms");

            var response = new ComparisonResponseDTO
            {
                Comparisons = comparisons,
                TotalRows = totalRows,
                ValidRows = validationResult.ValidRows,
                InvalidRows = validationResult.InvalidRows,
                Errors = skipInvalidRows ? validationResult.Errors : new List<string>(),
                Warnings = validationResult.Warnings,
                ValidationTimeMs = validationTimeMs,
                ComparisonTimeMs = comparisonTimeMs,
                TotalTimeMs = stopwatch.ElapsedMilliseconds,
                FileName = file.FileName
            };

            return Ok(response);
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

    [HttpPost("upload-departments")]
    public async Task<ActionResult<List<CSVDepartmentComparisionDTO>>> UploadAndCompareDepartments(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded" });
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "File must be a CSV file" });
        }

        try
        {
            var csvDepartments = new List<CSVDepartmentDTO>();
            var errors = new List<string>();

            using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = null
            }))
            {
                csv.Context.RegisterClassMap<CsvDepartmentMap>();
                if (!csv.Read() || !csv.ReadHeader())
                {
                    return BadRequest(new { error = "CSV file is empty or invalid" });
                }

                var header = csv.Context.Reader?.HeaderRecord ?? Array.Empty<string>();
                var hasNameColumn = header.Any(h => string.Equals(h?.Trim(), "name", StringComparison.OrdinalIgnoreCase));
                if (!hasNameColumn)
                {
                    errors.Add("CSV must contain a 'name' column.");
                }

                while (csv.Read())
                {
                    var fieldCount = csv.Context.Parser?.Count ?? 0;
                    if (fieldCount != 1)
                    {
                        var rowNumber = csv.Context.Parser?.Row ?? 0;
                        errors.Add($"Row {rowNumber}: CSV must contain exactly one column (name). Found {fieldCount} columns.");
                        continue;
                    }

                    var record = csv.GetRecord<CSVDepartmentDTO>();
                    if (record != null)
                    {
                        csvDepartments.Add(record);
                    }
                }
            }

            if (errors.Count > 0)
            {
                return BadRequest(new { errors });
            }

            if (csvDepartments.Count == 0)
            {
                return BadRequest(new { error = "CSV file is empty or invalid" });
            }

            var comparisons = await _csvSyncService.CompareDepartmentsWithDatabase(csvDepartments);
            _logger.LogInformation($"Compared CSV with {csvDepartments.Count} departments, found {comparisons.Count} comparisons");

            return Ok(comparisons);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing CSV file");
            return StatusCode(500, new { error = $"Error processing CSV: {ex.Message}" });
        }
    }

    [HttpPost("sync-departments")]
    public async Task<ActionResult<SyncResultDTO>> SyncDepartments([FromBody] DepartmentSyncRequestDTO syncRequest)
    {
        if (syncRequest?.Items == null || syncRequest.Items.Count == 0)
        {
            return BadRequest(new { error = "No sync items provided" });
        }

        try
        {
            var result = await _csvSyncService.SyncDepartments(syncRequest.Items);
            _logger.LogInformation($"Department sync completed: {result.RecordsProcessed} processed, {result.RecordsFailed} failed");

            if (!result.Success)
            {
                return StatusCode(207, result); // 207 Multi-Status for partial success
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing departments");
            return StatusCode(500, new { error = $"Error syncing departments: {ex.Message}" });
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
        Map(m => m.FirstName).Name("FirstName", "First Name", "first_name").Convert(args => args.Row.GetField("FirstName")?.Trim() ?? string.Empty);
        Map(m => m.LastName).Name("LastName", "Last Name", "last_name").Convert(args => args.Row.GetField("LastName")?.Trim() ?? string.Empty);
        Map(m => m.Email).Name("Email", "email").Convert(args => args.Row.GetField("Email")?.Trim() ?? string.Empty);
        Map(m => m.DepartmentName).Name("DepartmentName", "Department Name", "Department", "department_name", "department").Convert(args => args.Row.GetField("DepartmentName")?.Trim() ?? string.Empty);
        Map(m => m.AssignedToEmail).Name("AssignedToEmail", "Assigned To Email", "Line Manager Email", "Manager Email", "assigned_to_email", "manager_email").Optional().Convert(args => args.Row.GetField("AssignedToEmail")?.Trim());
    }
}

public sealed class CsvDepartmentMap : ClassMap<CSVDepartmentDTO>
{
    public CsvDepartmentMap()
    {
        Map(m => m.Name).Name("Name", "DepartmentName", "Department Name", "department_name", "department").Convert(args => args.Row.GetField("Name")?.Trim() ?? string.Empty);
    }
}
