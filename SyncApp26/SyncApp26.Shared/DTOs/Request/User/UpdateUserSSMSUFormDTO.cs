namespace SyncApp26.Shared.DTOs.Request.User
{
    public class UpdateUserSSMSUFormDTO
    {
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
    }
}
