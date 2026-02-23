namespace SyncApp26.Shared.DTOs.Response.Department
{
    public class DepartmentGETResponseDTO
    {
        public Guid Id { get; set; }
        public required string Name { get; set; }
        public bool IsActive { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}