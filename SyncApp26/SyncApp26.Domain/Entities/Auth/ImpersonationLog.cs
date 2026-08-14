using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// Immutable audit row: one per issued impersonation token. Never updated or deleted.
    /// No EndedAt — leaving impersonation is a pure client-side session swap (see
    /// ImpersonationService/plan), so there is no server event to record an end for.
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
