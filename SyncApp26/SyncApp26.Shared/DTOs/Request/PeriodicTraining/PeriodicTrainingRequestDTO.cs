using System;
using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.Validation;

namespace SyncApp26.Shared.DTOs.Request.PeriodicTraining
{
    public class CreatePeriodicTrainingDTO
    {
        public Guid UserId { get; set; }
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }

        [StringLength(NameValidationConstants.FunctionMaxLength)]
        public string? Occupation { get; set; }

        public string? MaterialTaught { get; set; }
        /// <summary>Linked instructor account — the person who will be asked to sign this training.</summary>
        public Guid InstructorId { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "verifierName.pattern")]
        public string? VerifierName { get; set; }
    }

    public class UpdatePeriodicTrainingDTO
    {
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }

        [StringLength(NameValidationConstants.FunctionMaxLength)]
        public string? Occupation { get; set; }

        public string? MaterialTaught { get; set; }
        /// <summary>Linked instructor account — the person who will be asked to sign this training.</summary>
        public Guid InstructorId { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "verifierName.pattern")]
        public string? VerifierName { get; set; }
    }

    public class UpdatePrintExclusionDTO
    {
        public bool Excluded { get; set; }
    }
}
