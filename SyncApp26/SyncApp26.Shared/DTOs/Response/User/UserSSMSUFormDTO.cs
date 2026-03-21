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

        // Training fields
        public DateTime? IntroductoryTrainingDate { get; set; }
        public int? IntroductoryTrainingHours { get; set; }
        public string? IntroductoryTrainingInstructor { get; set; }
        public string? IntroductoryTrainingInstructorFunction { get; set; }
        public string? IntroductoryTrainingContent { get; set; }

        public DateTime? WorkplaceTrainingDate { get; set; }
        public string? WorkplaceTrainingLocation { get; set; }
        public int? WorkplaceTrainingHours { get; set; }
        public string? WorkplaceTrainingInstructor { get; set; }
        public string? WorkplaceTrainingInstructorFunction { get; set; }
        public string? WorkplaceTrainingContent { get; set; }

        public string? AdmittedByName { get; set; }
        public string? AdmittedByFunction { get; set; }
        public DateTime? AdmittedDate { get; set; }

        // Employment dates
        public DateTime? HireDate { get; set; }
        public DateTime CreatedAt { get; set; }
        // Latest training signature snippets to show in SSM/SU form
        public string? LatestInstructorSignature { get; set; }
        public string? LatestInstructorSignatureMethod { get; set; }
        public string? LatestVerifierSignature { get; set; }
        public string? LatestVerifierSignatureMethod { get; set; }
    }
}
