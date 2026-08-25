using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// One row per issued refresh token. Only a SHA-256 hash of the raw token is ever stored, so a
    /// database leak alone can't be used to mint a session - the raw value only ever lives in the
    /// httpOnly cookie and the moment it's first issued.
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

        // Inherited from the first token in the chain and never extended by rotation - a refresh
        // token caps a session at its original issue time plus its lifetime, not "however long it
        // keeps getting rotated".
        public DateTime ExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ConsumedAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        // Forms the rotation chain: set the first time this token is rotated, so a later reuse of
        // the same (already-consumed) token can be detected and the whole chain revoked.
        [MaxLength(64)]
        public string? ReplacedByTokenHash { get; set; }
    }
}
