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
        // Signature data (base64 or typed text) and signature method when available
        public string? UserSignatureData { get; set; }
        public string? UserSignatureMethod { get; set; }
        public string? InstructorSignature { get; set; }
        public string? InstructorSignatureMethod { get; set; }
        public string? VerifierSignature { get; set; }
        public string? VerifierSignatureMethod { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
