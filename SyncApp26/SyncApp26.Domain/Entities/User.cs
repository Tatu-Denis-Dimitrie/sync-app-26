namespace SyncApp26.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? FunctionId { get; set; }
        public Guid? AssignedToId { get; set; }
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

        // Navigation properties
        public Department Department { get; set; }
        public User? AssignedTo { get; set; }  // Line manager
        public ICollection<User> AssignedUsers { get; set; } = new List<User>();  // Direct reports
        public Function Function { get; set; }
    }
}