namespace SyncApp26.Shared.DTOs.Response.User
{
    public class UserLookupResponseDTO
    {
        public Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Email { get; set; }
        public string? DepartmentName { get; set; }
    }

    public class UserLookupPageDTO
    {
        public List<UserLookupResponseDTO> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }
}
