namespace SyncApp26.Shared.DTOs.Response.SignatureVerification
{
    public class SignatureVersionSummaryDTO
    {
        public Guid SignatureId { get; set; }

        /// <summary>Which SignatureCanonicalSerializer schema computed this signature's HMAC — not
        /// a resign counter. See SignatureRecord.Version.</summary>
        public int Version { get; set; }

        /// <summary>True when this is the most recently signed entry (by SignedAt) in its slot —
        /// unrelated to Version, which never orders resigns.</summary>
        public bool IsMostRecentSignature { get; set; }

        /// <summary>"User", "Manager", or "Admin".</summary>
        public string SignerRole { get; set; } = string.Empty;

        public Guid SignerUserId { get; set; }
        public string SignerFullNameSnapshot { get; set; } = string.Empty;
        public DateTimeOffset SignedAt { get; set; }

        /// <summary>"Valid", "Invalid", "ChainBroken", or "Legacy" — see SignatureVerificationStatusResponseDTO.</summary>
        public string Status { get; set; } = string.Empty;
    }
}
