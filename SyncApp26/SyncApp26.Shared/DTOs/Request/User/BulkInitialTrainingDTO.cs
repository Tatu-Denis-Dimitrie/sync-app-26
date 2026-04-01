using System;
using System.Collections.Generic;

namespace SyncApp26.Shared.DTOs.Request.User
{
    public class BulkInitialTrainingDTO
    {
        /// <summary>"SSM", "SU", or "Both"</summary>
        public string DocumentType { get; set; } = "Both";

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

        public Guid? SelectedDepartmentId { get; set; }
        public bool ApplyToAllUsers { get; set; } = true;
        public List<Guid> SelectedUserIds { get; set; } = new();
    }

    public class BulkInitialTrainingResultDTO
    {
        public int SuccessCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
