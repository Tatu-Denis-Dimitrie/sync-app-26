namespace SyncApp26.Shared.DTOs.Response.User
{
    public class UserSSMSUFormDTO
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PersonalId { get; set; } = string.Empty;

        // Department and Function info
        public string? DepartmentName { get; set; }
        public string? FunctionName { get; set; }
        public string? RoleName { get; set; }

        // Manager info
        public string? ManagerFirstName { get; set; }
        public string? ManagerLastName { get; set; }
        public string? ManagerFunctionName { get; set; }

        // SSM/SU specific fields
        public DateTime? DateOfBirth { get; set; }
        public string? PlaceOfBirth { get; set; }
        public string? Address { get; set; }
        public string? BloodGroup { get; set; }
        public string? BadgeNumber { get; set; }
        public string? Education { get; set; }
        public string? Qualifications { get; set; }
        public string? CommuteRoute { get; set; }
        public int? CommuteDurationMinutes { get; set; }

        // Employment dates
        public DateTime? HireDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
