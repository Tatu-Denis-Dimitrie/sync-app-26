using SyncApp26.Domain.Enums;

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
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        /// <summary>
        /// True only for accounts whose roster membership is owned by the CSV import pipeline.
        /// Absence from an imported CSV is evidence of departure ONLY for these; accounts created
        /// any other way (seeded, self-registered) are simply outside the CSV's scope and must never
        /// be proposed for deletion just because an HR export doesn't mention them.
        /// </summary>
        public bool IsCsvManaged { get; set; }

        // SSM/SU Form fields
        public DateTime? DateOfBirth { get; set; }
        public string? PlaceOfBirth { get; set; }
        public string? Address { get; set; }
        public BloodType? BloodType { get; set; }
        public string? BadgeNumber { get; set; }
        public string? Education { get; set; }
        public string? Qualifications { get; set; }
        public string? CommuteRoute { get; set; }
        public int? CommuteDurationMinutes { get; set; }

        public string? AdmittedByName { get; set; }
        public string? AdmittedByFunction { get; set; }
        public DateTime? AdmittedDate { get; set; }

        // Navigation properties
        public Department? Department { get; set; }
        public User? AssignedTo { get; set; }  // Line manager
        public ICollection<User> AssignedUsers { get; set; } = new List<User>();  // Direct reports
        public Function? Function { get; set; }
        public ICollection<PeriodicTraining> PeriodicTrainings { get; set; } = new List<PeriodicTraining>();
        public ICollection<UserInitialTraining> InitialTrainings { get; set; } = new List<UserInitialTraining>();

        /// <summary>The roles this user currently holds.</summary>
        public ICollection<UserRoleAssignment> RoleAssignments { get; set; } = new List<UserRoleAssignment>();
    }
}