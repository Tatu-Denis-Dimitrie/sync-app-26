using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// Stores first-employment (initial) training data per user per document type (SSM / SU).
    /// One row per (UserId, DocumentType) pair.
    /// </summary>
    public class UserInitialTraining
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        /// <summary>"SSM" or "SU"</summary>
        [Required, MaxLength(10)]
        public string DocumentType { get; set; } = string.Empty;

        // ── Instruire introductivă generală ──────────────────────────
        public DateTime? IntroductoryTrainingDate { get; set; }
        public int? IntroductoryTrainingHours { get; set; }
        [MaxLength(200)]
        public string? IntroductoryTrainingInstructor { get; set; }
        [MaxLength(200)]
        public string? IntroductoryTrainingInstructorFunction { get; set; }
        [MaxLength(2000)]
        public string? IntroductoryTrainingContent { get; set; }

        // ── Instruire la locul de muncă ──────────────────────────────
        public DateTime? WorkplaceTrainingDate { get; set; }
        [MaxLength(200)]
        public string? WorkplaceTrainingLocation { get; set; }
        public int? WorkplaceTrainingHours { get; set; }
        [MaxLength(200)]
        public string? WorkplaceTrainingInstructor { get; set; }
        [MaxLength(200)]
        public string? WorkplaceTrainingInstructorFunction { get; set; }
        [MaxLength(2000)]
        public string? WorkplaceTrainingContent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
