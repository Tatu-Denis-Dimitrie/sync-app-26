using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// One row per issued refresh token. Only a SHA-256 hash is stored, never the raw value.
    /// </summary>
    public class RefreshToken
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [Required]
        [MaxLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        // Inherited from the first token in the chain - rotation never extends it.
        public DateTime ExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ConsumedAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        // Forms the rotation chain, so a later reuse of this token can be detected.
        [MaxLength(64)]
        public string? ReplacedByTokenHash { get; set; }
    }
}
