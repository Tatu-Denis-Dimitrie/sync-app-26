using System.Text;
using SyncApp26.Application.Services;
using SyncApp26.Shared.DTOs;

namespace SyncApp26.Tests.Services.Sync
{
    public class CsvValidationServiceTests
    {
        private const string ValidHeader = "PersonalId,FirstName,LastName,Email,DepartmentName";
        private const string ValidRow = "P1,John,Doe,john.doe@example.com,Engineering";

        private static CsvValidationService CreateService() => new();

        private static Stream MakeStream(string content, bool withBom = false)
        {
            var encoding = new UTF8Encoding(withBom);
            var bytes = encoding.GetBytes(content);
            return new MemoryStream(bytes);
        }

        private static Task<CsvValidationResultDTO> Validate(string content, string fileName = "users.csv", HashSet<string>? existingDepartments = null, bool withBom = false) =>
            CreateService().ValidateCsvFile(MakeStream(content, withBom), fileName, existingDepartments);

        // ───────────────────────── file-level checks ─────────────────────────

        [Fact]
        public async Task ValidateCsvFile_NonCsvExtension_ReturnsInvalid()
        {
            var result = await Validate($"{ValidHeader}\n{ValidRow}", fileName: "users.txt");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains(".csv extension"));
        }

        [Fact]
        public async Task ValidateCsvFile_EmptyFile_ReturnsInvalid()
        {
            var result = await Validate("");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("CSV file is empty"));
        }

        [Fact]
        public async Task ValidateCsvFile_HeaderOnlyNoDataRows_ReturnsInvalid()
        {
            var result = await Validate(ValidHeader);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("only headers, no data rows"));
        }

        [Fact]
        public async Task ValidateCsvFile_ValidFile_ReturnsValid()
        {
            var result = await Validate($"{ValidHeader}\n{ValidRow}");

            Assert.True(result.IsValid);
            Assert.Equal(1, result.TotalRows);
            Assert.Equal(1, result.ValidRows);
            Assert.Equal(0, result.InvalidRows);
        }

        // ───────────────────────── header validation ─────────────────────────

        [Fact]
        public async Task ValidateCsvFile_MissingRequiredHeader_ReturnsInvalid()
        {
            var result = await Validate($"PersonalId,FirstName,LastName,DepartmentName\nP1,John,Doe,Engineering");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Missing required column: 'Email'"));
        }

        [Fact]
        public async Task ValidateCsvFile_DuplicateHeaders_ReturnsInvalid()
        {
            var result = await Validate($"PersonalId,FirstName,LastName,Email,Email,DepartmentName\nP1,John,Doe,a@b.com,a@b.com,Engineering");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Duplicate columns found"));
        }

        [Theory]
        [InlineData("Personal_Id,First Name,Last-Name,EMAIL,Department_Name")]
        [InlineData(" PersonalId , FirstName , LastName , Email , DepartmentName ")]
        public async Task ValidateCsvFile_HeaderNormalization_AcceptsSpacesUnderscoresDashesAndCase(string header)
        {
            var result = await Validate($"{header}\n{ValidRow}");

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateCsvFile_DepartmentHeaderAlias_NotRecognizedByRequiredColumnCheck()
        {
            // GetColumnMap accepts "Department" as an alias for DepartmentName when mapping columns,
            // but ValidateHeaders only checks for the literal "DepartmentName" normalized name, so a
            // header using the alias is rejected before column mapping is ever reached.
            var result = await Validate($"PersonalId,FirstName,LastName,Email,Department\nP1,John,Doe,john@example.com,Engineering");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Missing required column: 'DepartmentName'"));
        }

        // ───────────────────────── row-level: required fields ─────────────────────────

        [Theory]
        [InlineData(",John,Doe,john@example.com,Engineering", "PersonalId")]
        [InlineData("P1,,Doe,john@example.com,Engineering", "FirstName")]
        [InlineData("P1,John,,john@example.com,Engineering", "LastName")]
        [InlineData("P1,John,Doe,,Engineering", "Email")]
        [InlineData("P1,John,Doe,john@example.com,", "DepartmentName")]
        public async Task ValidateCsvFile_RequiredFieldBlank_ReturnsRowError(string row, string fieldName)
        {
            var result = await Validate($"{ValidHeader}\n{row}");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains($"{fieldName} is required and cannot be empty"));
        }

        [Theory]
        [InlineData("PersonalId", 51)]
        [InlineData("FirstName", 101)]
        [InlineData("LastName", 101)]
        [InlineData("DepartmentName", 101)]
        public async Task ValidateCsvFile_FieldExceedsMaxLength_ReturnsRowError(string fieldName, int length)
        {
            var overlong = new string('x', length);
            var values = new Dictionary<string, string> { ["PersonalId"] = "P1", ["FirstName"] = "John", ["LastName"] = "Doe", ["Email"] = "john@example.com", ["DepartmentName"] = "Engineering" };
            values[fieldName] = overlong;
            var row = string.Join(",", values["PersonalId"], values["FirstName"], values["LastName"], values["Email"], values["DepartmentName"]);

            var result = await Validate($"{ValidHeader}\n{row}");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("too long"));
        }

        [Fact]
        public async Task ValidateCsvFile_InvalidEmailFormat_ReturnsRowError()
        {
            var result = await Validate($"{ValidHeader}\nP1,John,Doe,not-an-email,Engineering");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("is not in valid format"));
        }

        [Fact]
        public async Task ValidateCsvFile_ColumnCountMismatch_ReturnsRowError()
        {
            var result = await Validate($"{ValidHeader}\nP1,John,Doe,john@example.com");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Expected 5 columns, found 4"));
        }

        // ───────────────────────── DepartmentName existence check ─────────────────────────

        [Fact]
        public async Task ValidateCsvFile_DepartmentNotInExistingDepartments_ReturnsRowError()
        {
            var result = await Validate($"{ValidHeader}\n{ValidRow}", existingDepartments: new HashSet<string> { "sales" });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("does not exist in the database"));
        }

        [Fact]
        public async Task ValidateCsvFile_DepartmentInExistingDepartments_CaseInsensitiveMatch_IsValid()
        {
            var result = await Validate($"{ValidHeader}\n{ValidRow}", existingDepartments: new HashSet<string> { "engineering" });

            Assert.True(result.IsValid);
        }

        // ───────────────────────── optional fields: warnings, not errors ─────────────────────────

        [Fact]
        public async Task ValidateCsvFile_InvalidAssignedToPersonalIdFormat_AddsWarningNotError()
        {
            var header = $"{ValidHeader},AssignedToPersonalId";
            var result = await Validate($"{header}\nP1,John,Doe,john@example.com,Engineering,not-a-guid");

            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("AssignedToPersonalId"));
        }

        [Fact]
        public async Task ValidateCsvFile_FunctionTooLong_AddsWarningNotError()
        {
            var header = $"{ValidHeader},Function";
            var longFunction = new string('x', 101);
            var result = await Validate($"{header}\nP1,John,Doe,john@example.com,Engineering,{longFunction}");

            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("Function is too long"));
        }

        // ───────────────────────── CSV parsing: quoted fields ─────────────────────────

        [Fact]
        public async Task ValidateCsvFile_QuotedFieldWithEmbeddedCommaAndEscapedQuote_ParsedAsSingleValue()
        {
            var result = await Validate($"{ValidHeader}\nP1,John,\"Doe, Jr. \"\"The Second\"\"\",john@example.com,Engineering");

            Assert.True(result.IsValid);
        }

        // ───────────────────────── error cap ─────────────────────────

        [Fact]
        public async Task ValidateCsvFile_MoreThan20InvalidRows_TruncatesErrorListWithSummary()
        {
            var rows = Enumerable.Range(1, 25).Select(i => $"P{i},,Doe,john{i}@example.com,Engineering");
            var content = $"{ValidHeader}\n{string.Join("\n", rows)}";

            var result = await Validate(content);

            Assert.False(result.IsValid);
            Assert.Equal(21, result.Errors.Count);
            Assert.Contains("more errors", result.Errors[^1]);
        }
    }
}
