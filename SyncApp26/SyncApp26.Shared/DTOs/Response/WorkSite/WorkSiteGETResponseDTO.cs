namespace SyncApp26.Shared.DTOs.Response.WorkSite
{
    public class WorkSiteGETResponseDTO
    {
        public Guid Id { get; set; }
        public required string Name { get; set; }
        public bool IsActive { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
