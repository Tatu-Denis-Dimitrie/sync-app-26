namespace SyncApp26.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? FunctionId { get; set; }
        public Guid? AssignedToId { get; set; }
        public Guid RoleId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PersonalId { get; set; }
        public string? PasswordHash { get; set; }
        public bool? IsEmailVerified { get; set; }
        public string? EmailVerificationToken { get; set; }
        public DateTime? EmailVerificationTokenExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        // SSM/SU Form fields
        public DateTime? DateOfBirth { get; set; }
        public string? PlaceOfBirth { get; set; }
        public string? Address { get; set; }
        public string? BloodGroup { get; set; }
        public string? BadgeNumber { get; set; }
        public string? Education { get; set; }
        public string? Qualifications { get; set; }
        public string? CommuteRoute { get; set; }
        public int? CommuteDurationMinutes { get; set; }

        // Navigation properties
        public Department? Department { get; set; }
        public Role? Role { get; set; }
        public User? AssignedTo { get; set; }  // Line manager
        public ICollection<User> AssignedUsers { get; set; } = new List<User>();  // Direct reports
        public Function? Function { get; set; }
    }
}