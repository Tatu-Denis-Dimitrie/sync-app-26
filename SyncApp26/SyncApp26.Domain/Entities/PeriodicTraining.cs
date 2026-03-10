using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    public class PeriodicTraining
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        /// <summary>
        /// Date of the training session
        /// </summary>
        public DateTime? TrainingDate { get; set; }

        /// <summary>
        /// Duration in hours
        /// </summary>
        public decimal? DurationHours { get; set; }

        /// <summary>
        /// Occupation / Specialization (Ocupația / Specialitatea)
        /// </summary>
        [MaxLength(200)]
        public string? Occupation { get; set; }

        /// <summary>
        /// Material taught (Materialul predat)
        /// </summary>
        [MaxLength(500)]
        public string? MaterialTaught { get; set; }

        /// <summary>
        /// Employee (trainee) signature data — base64 image or typed text
        /// </summary>
        public string? UserSignatureData { get; set; }

        /// <summary>
        /// Employee signature method: "Draw" or "Type"
        /// </summary>
        [MaxLength(50)]
        public string? UserSignatureMethod { get; set; }

        /// <summary>
        /// Instructor/manager signature data (base64 or identifier)
        /// </summary>
        public string? InstructorSignature { get; set; }

        /// <summary>
        /// Instructor/manager signature method: "Draw" or "Type"
        /// </summary>
        [MaxLength(50)]
        public string? InstructorSignatureMethod { get; set; }

        /// <summary>
        /// Verifier signature (for SSM only)
        /// </summary>
        public string? VerifierSignature { get; set; }

        /// <summary>
        /// Instructor name
        /// </summary>
        [MaxLength(200)]
        public string? InstructorName { get; set; }

        /// <summary>
        /// Verifier name (for SSM)
        /// </summary>
        [MaxLength(200)]
        public string? VerifierName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
