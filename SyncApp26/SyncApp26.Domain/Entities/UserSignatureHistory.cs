using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// Immutable audit log for every create / update / revoke action on a user's stored signature.
    /// Rows are never updated or deleted — they form the auditable chain of custody.
    /// </summary>
    public class UserSignatureHistory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        /// <summary>Snapshot of the signature image data at the time of the action.</summary>
        [Required]
        public string SignatureData { get; set; } = string.Empty;

        [MaxLength(50)]
        public string SignatureMethod { get; set; } = string.Empty;

        /// <summary>SHA-256 hex digest of SignatureData at the time of the action.</summary>
        [Required]
        [MaxLength(64)]
        public string SignatureHash { get; set; } = string.Empty;

        /// <summary>Server-issued RSA cryptographic proof identical to UserSignature.CryptographicProof.</summary>
        public string? CryptographicProof { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        /// <summary>"Created", "Updated", or "Revoked"</summary>
        [Required]
        [MaxLength(20)]
        public string Action { get; set; } = string.Empty;

        /// <summary>The user who performed the action (always the signature owner for now).</summary>
        [Required]
        public Guid PerformedByUserId { get; set; }

        [Required]
        [MaxLength(255)]
        public string PerformedByEmail { get; set; } = string.Empty;

        /// <summary>Immutable creation timestamp. Never modified after insert.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
