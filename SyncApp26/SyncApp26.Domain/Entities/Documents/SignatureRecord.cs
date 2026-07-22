using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// One row per act of signing. Unlike the flat *SignatureData/*SignedAt columns on
    /// UserDocument, every *Snapshot field here is captured once, at signing time, and never
    /// updated afterwards — verification recomputes SignatureHmac strictly from these frozen
    /// values, never from the live User/PeriodicTraining rows they were copied from.
    /// </summary>
    public class SignatureRecord
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserDocumentId { get; set; }

        [ForeignKey("UserDocumentId")]
        public virtual UserDocument UserDocument { get; set; } = null!;

        public Guid? PeriodicTrainingId { get; set; }

        [ForeignKey("PeriodicTrainingId")]
        public virtual PeriodicTraining? PeriodicTraining { get; set; }

        /// <summary>"User", "Manager", or "Admin" — which role in the signing workflow this is.</summary>
        [Required]
        [MaxLength(20)]
        public string SignerRole { get; set; } = string.Empty;

        /// <summary>The person who physically performed the signing action.</summary>
        [Required]
        public Guid SignerUserId { get; set; }

        [ForeignKey("SignerUserId")]
        public virtual User SignerUser { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string SignerFullNameSnapshot { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string SignerPositionSnapshot { get; set; } = string.Empty;

        /// <summary>"Draw" or "Type"</summary>
        [MaxLength(50)]
        public string SignatureMethod { get; set; } = string.Empty;

        /// <summary>Base64-encoded image (Draw) or the typed name (Type).</summary>
        [Required]
        public string SignatureData { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? MaterialTaughtSnapshot { get; set; }

        public decimal? DurationHoursSnapshot { get; set; }

        public DateTime? TrainingDateSnapshot { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        public DateTimeOffset SignedAt { get; set; }

        /// <summary>SignatureHmac of this SignerUserId's previous SignatureRecord, chronologically.</summary>
        [MaxLength(64)]
        public string? PreviousSignatureHash { get; set; }

        /// <summary>Null only for legacy rows backfilled before this mechanism existed.</summary>
        [MaxLength(64)]
        public string? SignatureHmac { get; set; }

        /// <summary>True for backfilled rows with no real HMAC — never treat these as verified.</summary>
        public bool IsLegacyUnverified { get; set; } = false;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
