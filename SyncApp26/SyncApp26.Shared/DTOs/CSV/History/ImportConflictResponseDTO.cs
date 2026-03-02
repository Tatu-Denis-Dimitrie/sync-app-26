namespace SyncApp26.Shared.DTOs.CSV.History
{
    public class UserChangeHistoryResponseDTO
    {
        public Guid Id { get; set; }
        public Guid ImportHistoryId { get; set; }
        public DateTime? ImportDate { get; set; }
        public string? ImportFileName { get; set; }
        public Guid UserId { get; set; }
        public string FieldName { get; set; } //department, line manager
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Status { get; set; } //accepted, rejected
    }
}