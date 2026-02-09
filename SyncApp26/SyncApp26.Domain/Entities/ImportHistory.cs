namespace SyncApp26.Domain.Entities
{
    public class ImportHistory
    {
        public Guid Id { get; set; }
        public DateTime ImportDate { get; set; }
        public string FileName { get; set; }
    }
}