namespace SyncApp26.Shared.DTOs.CSV.History
{
    public class UserChangeHistoryResponseDTO
    {
        public Guid Id { get; set; }
        public Guid? ImportHistoryId { get; set; }
        public DateTime? ImportDate { get; set; }
        public string? ImportFileName { get; set; }
        public Guid UserId { get; set; }
        public string FieldName { get; set; } = string.Empty; //department, line manager
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public string? Status { get; set; } //accepted, rejected
        public DateTime CreatedAt { get; set; }
    }

    public class UserChangeHistoryPageDTO
    {
        public List<UserChangeHistoryResponseDTO> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }
}