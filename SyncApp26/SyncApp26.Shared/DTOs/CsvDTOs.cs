using SyncApp26.Shared.DTOs.Response.User;

namespace SyncApp26.Shared.DTOs;

public class CsvUserDTO
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string DepartmentName { get; set; }
    public string? AssignedToEmail { get; set; } // Line manager email for lookup
}

public class UserComparisonDTO
{
    public required string Id { get; set; }
    public required string Status { get; set; } // "new", "modified", "unchanged", "deleted"
    public UserGETResponseDTO? DbUser { get; set; }
    public CsvUserDataDTO? CsvUser { get; set; }
    public List<FieldConflictDTO> Conflicts { get; set; } = new();
    public bool Selected { get; set; }
}

public class CsvUserDataDTO
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string DepartmentName { get; set; }
    public string? AssignedToEmail { get; set; } // Line manager email
    public string? AssignedToName { get; set; } // Line manager display name
}

public class FieldConflictDTO
{
    public required string Field { get; set; }
    public object? DbValue { get; set; }
    public object? CsvValue { get; set; }
    public string? SelectedValue { get; set; } // "db" or "csv"
    public bool Selected { get; set; }
}

public class SyncRequestDTO
{
    public List<UserSyncItemDTO> Items { get; set; } = new();
    public string? FileName { get; set; }
}

public class UserSyncItemDTO
{
    public required string Id { get; set; }
    public required string Status { get; set; } // "new", "modified", "deleted"
    public CsvUserDTO? CsvData { get; set; }
    public List<FieldConflictDTO> Conflicts { get; set; } = new();
}

public class SyncResultDTO
{
    public bool Success { get; set; }
    public int RecordsProcessed { get; set; }
    public int RecordsFailed { get; set; }
    public int RecordsSkipped { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = new();
    public long ProcessingTimeMs { get; set; }
}

public class ComparisonResponseDTO
{
    public List<UserComparisonDTO> Comparisons { get; set; } = new();
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public long ValidationTimeMs { get; set; }
    public long ComparisonTimeMs { get; set; }
    public long TotalTimeMs { get; set; }
    public string? FileName { get; set; }
}
