using System;

namespace SyncApp26.Shared.DTOs.Response.PeriodicTraining
{
    public class PeriodicTrainingResponseDTO
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }
        public string? Occupation { get; set; }
        public string? MaterialTaught { get; set; }
        public string? InstructorName { get; set; }
        public string? VerifierName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
