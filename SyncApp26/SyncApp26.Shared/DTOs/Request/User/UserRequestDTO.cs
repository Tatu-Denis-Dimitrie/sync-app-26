using SyncApp26.Domain.Enums;

namespace SyncApp26.Shared.DTOs.Request.User
{
    public class UserRequestDTO
    {
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Email { get; set; }
        public Guid DepartmentId { get; set; }
        public string? Function { get; set; }
        public Guid? AssignedToId { get; set; }
        public UserRole? Role { get; set; }
    }
}
