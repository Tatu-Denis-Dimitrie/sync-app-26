using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.Validation;

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

        [StringLength(NameValidationConstants.NameMaxLength)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "Name must contain letters only (spaces, hyphens and apostrophes allowed).")]
        public string? AdmittedByName { get; set; }

        [StringLength(NameValidationConstants.FunctionMaxLength)]
        public string? AdmittedByFunction { get; set; }

        public DateTime? AdmittedDate { get; set; }

        // Per-type initial training (documentType = "SSM" or "SU")
        public List<InitialTrainingEntryDTO> InitialTrainings { get; set; } = new();
    }

    public class InitialTrainingEntryDTO
    {
        /// <summary>"SSM" or "SU"</summary>
        public string DocumentType { get; set; } = string.Empty;

        public DateTime? IntroductoryTrainingDate { get; set; }
        public int? IntroductoryTrainingHours { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "Instructor name must contain letters only (spaces, hyphens and apostrophes allowed).")]
        public string? IntroductoryTrainingInstructor { get; set; }

        [StringLength(NameValidationConstants.FunctionMaxLength)]
        public string? IntroductoryTrainingInstructorFunction { get; set; }

        public string? IntroductoryTrainingContent { get; set; }

        public DateTime? WorkplaceTrainingDate { get; set; }
        public string? WorkplaceTrainingLocation { get; set; }
        public int? WorkplaceTrainingHours { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "Instructor name must contain letters only (spaces, hyphens and apostrophes allowed).")]
        public string? WorkplaceTrainingInstructor { get; set; }

        [StringLength(NameValidationConstants.FunctionMaxLength)]
        public string? WorkplaceTrainingInstructorFunction { get; set; }

        public string? WorkplaceTrainingContent { get; set; }
    }
}
