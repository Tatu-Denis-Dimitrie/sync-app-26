using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// Immutable audit row: one per issued impersonation token. Never updated or deleted.
    /// No EndedAt — SessionController.StopImpersonation mints a fresh token rather than updating
    /// this row, and impersonation can also simply expire (30 min) without an explicit stop, so
    /// there's no single server event that would reliably mark an end.
    /// </summary>
    public class ImpersonationLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ImpersonatorUserId { get; set; }

        [ForeignKey("ImpersonatorUserId")]
        public virtual User ImpersonatorUser { get; set; } = null!;

        [Required]
        public Guid TargetUserId { get; set; }

        [ForeignKey("TargetUserId")]
        public virtual User TargetUser { get; set; } = null!;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string? IpAddress { get; set; }
    }
}
