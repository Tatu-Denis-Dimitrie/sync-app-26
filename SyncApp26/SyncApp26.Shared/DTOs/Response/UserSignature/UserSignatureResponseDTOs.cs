namespace SyncApp26.Shared.DTOs.Response.UserSignature
{
    public class UserSignatureResponseDTO
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string SignatureData { get; set; } = string.Empty;
        public string SignatureMethod { get; set; } = string.Empty;

        /// <summary>SHA-256 hex digest of SignatureData — clients can verify integrity locally.</summary>
        public string SignatureHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class UserSignatureHistoryResponseDTO
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string SignatureMethod { get; set; } = string.Empty;
        public string SignatureHash { get; set; } = string.Empty;

        /// <summary>"Created", "Updated", or "Revoked"</summary>
        public string Action { get; set; } = string.Empty;

        public string? IpAddress { get; set; }
        public Guid PerformedByUserId { get; set; }
        public string PerformedByEmail { get; set; } = string.Empty;

        /// <summary>Immutable timestamp of the audit event.</summary>
        public DateTime CreatedAt { get; set; }
    }
}
