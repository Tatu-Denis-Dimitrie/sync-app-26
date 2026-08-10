namespace SyncApp26.Shared.DTOs.Request.User
{
    public class CreateRoleRequestDTO
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
    }
}
