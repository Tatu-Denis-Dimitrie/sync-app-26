namespace SyncApp26.Shared.DTOs.Response.User
{
    public class RoleResponseDTO
    {
        public Guid Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public bool IsSystem { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
