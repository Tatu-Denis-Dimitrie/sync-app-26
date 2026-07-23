namespace SyncApp26.Shared.DTOs.Response.SignatureVerification
{
    public class SignatureVerificationStatusResponseDTO
    {
        public Guid SignatureId { get; set; }

        /// <summary>"Valid", "Invalid", "ChainBroken", "Legacy", or "NotFound".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>True when the stored SignatureHmac recomputes from the frozen snapshot fields.</summary>
        public bool IsHashValid { get; set; }

        /// <summary>True when PreviousSignatureHash matches this signer's actual prior SignatureRecord.</summary>
        public bool IsChainValid { get; set; }

        /// <summary>True for backfilled rows with no real HMAC — never treated as verified.</summary>
        public bool IsLegacy { get; set; }

        public DateTimeOffset VerifiedAt { get; set; }
    }
}
