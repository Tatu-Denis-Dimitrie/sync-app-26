namespace SyncApp26.Shared.DTOs;

public class CsvValidationResultDTO
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
}

public class CsvValidationErrorDTO
{
    public int Row { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
