namespace SyncApp26.Shared.DTOs.Request.Department
{
    public class DepartmentRequestDTO
    {
        public required string Name { get; set; }
        public bool IsActive { get; set; } = true;
    }
}