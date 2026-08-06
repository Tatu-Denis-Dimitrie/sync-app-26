using System;

namespace SyncApp26.Shared.DTOs.Response.PeriodicTraining
{
    public class PeriodicTrainingResponseDTO
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        /// <summary>"SSM" or "SU" — which document type this training row belongs to.</summary>
        public string? DocumentType { get; set; }
        /// <summary>Set when this row is a display copy carried over from an earlier session by
        /// CopyHistoricalPeriodicTrainingRowsAsync — the copy has no SignatureRecord of its own;
        /// the row identified by this id is the one with the real signature history.</summary>
        public Guid? SourceRowId { get; set; }
        public DateTime? TrainingDate { get; set; }
        public decimal? DurationHours { get; set; }
        public string? Occupation { get; set; }
        public string? MaterialTaught { get; set; }
        public Guid? InstructorId { get; set; }
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
        public DateTime? ExcludedFromPrintAt { get; set; }
    }
}
