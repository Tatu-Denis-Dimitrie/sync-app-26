using System;

namespace SyncApp26.Shared.DTOs.Request.PeriodicTraining
{
    public class CreatePeriodicTrainingDTO
    {
        public Guid UserId { get; set; }
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }
        public string? Occupation { get; set; }
        public string? MaterialTaught { get; set; }
        /// <summary>Linked instructor account — the person who will be asked to sign this training.</summary>
        public Guid InstructorId { get; set; }
        public string? VerifierName { get; set; }
    }

    public class UpdatePeriodicTrainingDTO
    {
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }
        public string? Occupation { get; set; }
        public string? MaterialTaught { get; set; }
        /// <summary>Linked instructor account — the person who will be asked to sign this training.</summary>
        public Guid InstructorId { get; set; }
        public string? VerifierName { get; set; }
    }
}
