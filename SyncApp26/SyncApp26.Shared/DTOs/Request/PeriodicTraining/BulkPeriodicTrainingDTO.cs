using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.Validation;

namespace SyncApp26.Shared.DTOs.Request.PeriodicTraining
{
    public class BulkCreatePeriodicTrainingDTO
    {
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }

        [StringLength(NameValidationConstants.FunctionMaxLength)]
        public string? Occupation { get; set; }

        public string? MaterialTaught { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "verifierName.pattern")]
        public string? VerifierName { get; set; }

        public string DocumentType { get; set; } = "Both"; // "SSM", "SU", or "Both"
        public Guid? SelectedDepartmentId { get; set; }
        public bool ApplyToAllUsers { get; set; } = true;
        public List<Guid> SelectedUserIds { get; set; } = new();
    }

    public class BulkCreateResultDTO
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
