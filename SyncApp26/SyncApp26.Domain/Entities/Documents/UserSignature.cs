using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// Stores the current active personal signature for a user.
    /// One row per user. Each save creates an immutable audit entry in UserSignatureHistory.
    /// </summary>
    public class UserSignature
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        /// <summary>Base64-encoded image (PNG/SVG) of the drawn or typed signature.</summary>
        [Required]
        public string SignatureData { get; set; } = string.Empty;

        /// <summary>"Draw" or "Type"</summary>
        [MaxLength(50)]
        public string SignatureMethod { get; set; } = string.Empty;

        /// <summary>SHA-256 hex digest of SignatureData — used for integrity verification.</summary>
        [Required]
        [MaxLength(64)]
        public string SignatureHash { get; set; } = string.Empty;

        /// <summary>
        /// RSA signature (Base64) over the canonical string
        /// "{SignatureHash}|{UserId}|{TimestampUtcTicks}" produced by the server's private key.
        /// Allows offline cryptographic proof that this signature was accepted by the server.
        /// </summary>
        public string? CryptographicProof { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Non-null when the user has revoked their stored signature.</summary>
        public DateTime? RevokedAt { get; set; }
    }
}
