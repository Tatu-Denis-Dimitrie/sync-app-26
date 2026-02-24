using System.Text;
using System.Text.RegularExpressions;
using SyncApp26.Shared.DTOs;
using SyncApp26.Application.IServices;

namespace SyncApp26.Application.Services;

public class CsvValidationService : ICsvValidationService
{
    private static readonly string[] RequiredHeaders = { "PersonalId", "FirstName", "LastName", "Email", "DepartmentName" };
    private static readonly string[] OptionalHeaders = { "AssignedToPersonalId" };
    private static readonly Regex EmailRegex = new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);

    public async Task<CsvValidationResultDTO> ValidateCsvFile(Stream fileStream, string fileName, HashSet<string>? existingDepartments = null)
    {
        var result = new CsvValidationResultDTO { IsValid = true };

        try
        {
            // Check file extension
            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = false;
                result.Errors.Add("File must have .csv extension");
                return result;
            }

            // Read file and detect encoding
            fileStream.Position = 0;
            var buffer = new byte[fileStream.Length];
            await fileStream.ReadExactlyAsync(buffer.AsMemory(0, buffer.Length));
            fileStream.Position = 0;

            // Check for BOM and validate UTF-8
            var encoding = DetectEncoding(buffer);
            if (encoding == null)
            {
                result.IsValid = false;
                result.Errors.Add("Invalid file encoding. File must be UTF-8 encoded.");
                return result;
            }

            // Check for invalid characters
            var content = encoding.GetString(buffer);
            if (HasInvalidCharacters(content, out var invalidChars))
            {
                result.IsValid = false;
                result.Errors.Add($"File contains invalid special characters: {string.Join(", ", invalidChars.Take(10))}");
                return result;
            }

            // Parse CSV lines
            using var reader = new StreamReader(fileStream, encoding);
            var lines = new List<string>();
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            if (lines.Count == 0)
            {
                result.IsValid = false;
                result.Errors.Add("CSV file is empty");
                return result;
            }

            if (lines.Count == 1)
            {
                result.IsValid = false;
                result.Errors.Add("CSV file contains only headers, no data rows");
                return result;
            }

            // Validate headers
            var headerLine = lines[0];
            var headers = ParseCsvLine(headerLine);

            var headerValidation = ValidateHeaders(headers);
            if (!headerValidation.isValid)
            {
                result.IsValid = false;
                result.Errors.AddRange(headerValidation.errors);
                return result;
            }

            // Get column indices
            var columnMap = GetColumnMap(headers);

            // Validate data rows
            result.TotalRows = lines.Count - 1; // Exclude header
            var rowNumber = 1; // Start from 1 (header is 0)

            for (int i = 1; i < lines.Count; i++)
            {
                rowNumber = i;
                var dataLine = lines[i];
                var values = ParseCsvLine(dataLine);

                var rowValidation = ValidateDataRow(rowNumber, values, columnMap, headers.Count, existingDepartments);
                if (rowValidation.errors.Count > 0)
                {
                    result.InvalidRows++;
                    result.InvalidRowNumbers.Add(rowNumber);
                    result.Errors.AddRange(rowValidation.errors);
                    result.IsValid = false;
                }
                else
                {
                    result.ValidRows++;
                }

                if (rowValidation.warnings.Count > 0)
                {
                    result.Warnings.AddRange(rowValidation.warnings);
                }
            }

            // Set overall validation result
            if (result.Errors.Count > 20)
            {
                var remainingErrors = result.Errors.Count - 20;
                result.Errors = result.Errors.Take(20).ToList();
                result.Errors.Add($"...and {remainingErrors} more errors");
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Error reading CSV file: {ex.Message}");
            return result;
        }
    }

    private Encoding? DetectEncoding(byte[] buffer)
    {
        // Check for UTF-8 BOM
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        // Try to validate as UTF-8
        try
        {
            var decoder = Encoding.UTF8.GetDecoder();
            var charBuffer = new char[buffer.Length];
            decoder.GetChars(buffer, 0, buffer.Length, charBuffer, 0, true);
            return Encoding.UTF8;
        }
        catch
        {
            return null;
        }
    }

    private bool HasInvalidCharacters(string content, out List<string> invalidChars)
    {
        invalidChars = new List<string>();

        // Check for null bytes and other control characters that shouldn't be in CSV
        var invalidControlChars = new[] { '\0', '\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07', '\x08', '\x0B', '\x0C', '\x0E', '\x0F' };

        foreach (var ch in content)
        {
            if (invalidControlChars.Contains(ch))
            {
                var hex = ((int)ch).ToString("X2");
                if (!invalidChars.Contains($"0x{hex}"))
                {
                    invalidChars.Add($"0x{hex}");
                }
            }
        }

        return invalidChars.Count > 0;
    }

    private List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var currentValue = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentValue.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(currentValue.ToString().Trim());
                currentValue.Clear();
            }
            else
            {
                currentValue.Append(c);
            }
        }

        values.Add(currentValue.ToString().Trim());
        return values;
    }

    private (bool isValid, List<string> errors) ValidateHeaders(List<string> headers)
    {
        var errors = new List<string>();

        // Normalize headers for comparison (case-insensitive, trim spaces, handle underscores)
        var normalizedHeaders = headers.Select(h => NormalizeHeaderName(h)).ToList();

        // Check for required headers
        foreach (var required in RequiredHeaders)
        {
            var normalized = NormalizeHeaderName(required);
            if (!normalizedHeaders.Contains(normalized))
            {
                errors.Add($"Missing required column: '{required}'. CSV must contain columns: {string.Join(", ", RequiredHeaders)}");
            }
        }

        // Check for duplicate headers
        var duplicates = normalizedHeaders.GroupBy(h => h).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Any())
        {
            errors.Add($"Duplicate columns found: {string.Join(", ", duplicates)}");
        }

        return (errors.Count == 0, errors);
    }

    private string NormalizeHeaderName(string header)
    {
        return header.Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .ToLower();
    }

    private Dictionary<string, int> GetColumnMap(List<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeaderName(headers[i]);

            // Map to standard field names
            if (normalized == NormalizeHeaderName("PersonalId") || normalized == "personalid" || normalized == "personal_id")
                map["PersonalId"] = i;
            if (normalized == NormalizeHeaderName("FirstName") || normalized == "firstname")
                map["FirstName"] = i;
            else if (normalized == NormalizeHeaderName("LastName") || normalized == "lastname")
                map["LastName"] = i;
            else if (normalized == "email")
                map["Email"] = i;
            else if (normalized == NormalizeHeaderName("DepartmentName") || normalized == "department")
                map["DepartmentName"] = i;
            else if (normalized == NormalizeHeaderName("AssignedToPersonalId") || normalized == "managerpersonalid" || normalized == "linemanagerpersonalid")
                map["AssignedToPersonalId"] = i;
        }

        return map;
    }

    private (List<string> errors, List<string> warnings) ValidateDataRow(int rowNumber, List<string> values, Dictionary<string, int> columnMap, int expectedColumnCount, HashSet<string>? existingDepartments = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check column count
        if (values.Count != expectedColumnCount)
        {
            errors.Add($"Row {rowNumber}: Expected {expectedColumnCount} columns, found {values.Count}");
            return (errors, warnings);
        }

        if (columnMap.TryGetValue("PersonalId", out int personalIdIdx))
        {
            var personalId = values[personalIdIdx];
            if (string.IsNullOrWhiteSpace(personalId))
            {
                errors.Add($"Row {rowNumber}: PersonalId is required and cannot be empty");
            }
            else if (personalId.Length > 50)
            {
                errors.Add($"Row {rowNumber}: PersonalId is too long (max 50 characters)");
            }
        }

        // Validate FirstName
        if (columnMap.TryGetValue("FirstName", out int firstNameIdx))
        {
            var firstName = values[firstNameIdx];
            if (string.IsNullOrWhiteSpace(firstName))
            {
                errors.Add($"Row {rowNumber}: FirstName is required and cannot be empty");
            }
            else if (firstName.Length > 100)
            {
                errors.Add($"Row {rowNumber}: FirstName is too long (max 100 characters)");
            }
        }

        // Validate LastName
        if (columnMap.TryGetValue("LastName", out int lastNameIdx))
        {
            var lastName = values[lastNameIdx];
            if (string.IsNullOrWhiteSpace(lastName))
            {
                errors.Add($"Row {rowNumber}: LastName is required and cannot be empty");
            }
            else if (lastName.Length > 100)
            {
                errors.Add($"Row {rowNumber}: LastName is too long (max 100 characters)");
            }
        }

        // Validate Email
        if (columnMap.TryGetValue("Email", out int emailIdx))
        {
            var email = values[emailIdx];
            if (string.IsNullOrWhiteSpace(email))
            {
                errors.Add($"Row {rowNumber}: Email is required and cannot be empty");
            }
            else if (!IsValidEmail(email))
            {
                errors.Add($"Row {rowNumber}: Email '{email}' is not in valid format");
            }
        }

        // Validate DepartmentName
        if (columnMap.TryGetValue("DepartmentName", out int deptIdx))
        {
            var department = values[deptIdx];
            if (string.IsNullOrWhiteSpace(department))
            {
                errors.Add($"Row {rowNumber}: DepartmentName is required and cannot be empty");
            }
            else if (department.Length > 100)
            {
                errors.Add($"Row {rowNumber}: DepartmentName is too long (max 100 characters)");
            }
            else if (existingDepartments != null && !existingDepartments.Contains(department.Trim().ToLower()))
            {
                errors.Add($"Row {rowNumber}: Department '{department}' does not exist in the database. Please create the department first.");
            }
        }

        // Validate AssignedToPersonalId (optional)
        if (columnMap.TryGetValue("AssignedToPersonalId", out int assignedIdx))
        {
            var assignedPersonalId = values[assignedIdx];
            if (!string.IsNullOrWhiteSpace(assignedPersonalId) && !Guid.TryParse(assignedPersonalId, out _))
            {
                warnings.Add($"Row {rowNumber}: AssignedToPersonalId '{assignedPersonalId}' is not in valid format");
            }
        }

        return (errors, warnings);
    }

    public bool IsValidEmail(string email)
    {
        return EmailRegex.IsMatch(email);
    }
}
