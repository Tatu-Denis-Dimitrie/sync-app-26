namespace SyncApp26.Shared.DTOs.Response.User
{
    public class UserGETResponseDTO
    {
        public Guid Id { get; set; }
        public required string PersonalId { get; set; }
        public Guid? RoleId { get; set; }
        public string? RoleName { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Email { get; set; }
        public Guid DepartmentId { get; set; }
        public required string DepartmentName { get; set; }
        public string? Function { get; set; }
        public Guid? AssignedToId { get; set; }
        public string? AssignedToName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
