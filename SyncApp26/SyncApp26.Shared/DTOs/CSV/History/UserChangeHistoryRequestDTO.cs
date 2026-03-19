namespace SyncApp26.Shared.DTOs.CSV.History
{
    public class UserChangeHistoryRequestDTO
    {
        public Guid? ImportHistoryId { get; set; }
        public Guid UserId { get; set; }
        public string FieldName { get; set; } //department, line manager
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string? Status { get; set; } //accepted, rejected
    }
}