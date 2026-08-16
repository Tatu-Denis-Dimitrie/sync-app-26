using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    /// <summary>
    /// One row per SignatureVerificationSweeper run that found anomalies (Invalid/ChainBroken
    /// signatures) — mirrors the live "SignatureAnomalyAlert" SignalR payload so an admin who
    /// wasn't connected when the sweep fired still sees it on next login instead of only via email.
    /// Per-signature detail is not persisted here; it stays in the sweep's log output and the
    /// (capped) admin alert email.
    /// </summary>
    public class SignatureAnomalyAlert
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public int RecordsChecked { get; set; }
        public int AnomaliesFound { get; set; }

        public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

        public bool IsRead { get; set; }
        public DateTimeOffset? ReadAt { get; set; }

        public Guid? ReadByAdminId { get; set; }

        [ForeignKey("ReadByAdminId")]
        public virtual User? ReadByAdmin { get; set; }
    }
}
