using System;
using System.Collections.Generic;

namespace SyncApp26.Shared.DTOs.Request.PeriodicTraining
{
    public class BulkCreatePeriodicTrainingDTO
    {
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }
        public string? Occupation { get; set; }
        public string? MaterialTaught { get; set; }
        public string? InstructorName { get; set; }
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
