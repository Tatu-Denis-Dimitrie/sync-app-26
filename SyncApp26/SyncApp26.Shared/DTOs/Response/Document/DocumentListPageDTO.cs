namespace SyncApp26.Shared.DTOs.Response.Document
{
    public class DocumentListPageDTO
    {
        public List<object> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }
}
