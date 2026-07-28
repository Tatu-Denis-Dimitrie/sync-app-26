namespace SyncApp26.Shared.DTOs.Response.SignatureVerification
{
    public class PeriodicTrainingSignatureHistoryDTO
    {
        public Guid PeriodicTrainingId { get; set; }

        /// <summary>The employee this training belongs to — not for display, used by the
        /// controller to apply the same self/admin/line-manager access rule as the other
        /// signature-verification endpoints.</summary>
        public Guid UserId { get; set; }

        /// <summary>Key: SignerRole ("User", "Manager", "Admin"). Each list is ordered by SignedAt ascending.</summary>
        public Dictionary<string, List<SignatureVersionSummaryDTO>> VersionsByRole { get; set; } = new();
    }
}
