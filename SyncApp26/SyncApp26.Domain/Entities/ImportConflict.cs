namespace SyncApp26.Domain.Entities
{
    public class ImportConflict
    {
        public Guid Id { get; set; }
        public Guid ImportHistoryId { get; set; }
        public Guid UserId { get; set; }
        public string FieldName { get; set; } //department, line manager
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Status { get; set; } //accepted, rejected


        public ImportHistory ImportHistory { get; set; }
        public User User { get; set; }
    }
}