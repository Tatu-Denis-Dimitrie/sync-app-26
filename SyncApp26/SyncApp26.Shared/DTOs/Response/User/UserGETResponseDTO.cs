using SyncApp26.Domain.Enums;

namespace SyncApp26.Shared.DTOs.Response.User
{
    public class UserGETResponseDTO
    {
        public Guid Id { get; set; }
        public required string PersonalId { get; set; }
        public List<string> Roles { get; set; } = new();
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Email { get; set; }
        public Guid DepartmentId { get; set; }
        public required string DepartmentName { get; set; }
        public string? Function { get; set; }
        public Guid? AssignedToId { get; set; }
        public string? AssignedToName { get; set; }
        public string? Address { get; set; }
        public string? BadgeNumber { get; set; }
        public BloodType? BloodType { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool HasSignedSsm { get; set; }
        public bool HasSignedSu { get; set; }
        public bool HasUnsignedSsm { get; set; }
        public bool HasUnsignedSu { get; set; }
    }
}
