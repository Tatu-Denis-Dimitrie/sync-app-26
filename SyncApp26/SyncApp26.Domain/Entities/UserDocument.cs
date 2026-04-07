using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SyncApp26.Domain.Entities
{
    public class UserDocument
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        [Required]
        [MaxLength(50)]
        public string DocumentType { get; set; } // e.g., "SSM", "SU"

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } // "PendingUser", "PendingManager", "Completed"

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string? PdfFilePath { get; set; }

        // --- Cryptographic Audit Fields ---
        [MaxLength(64)] // SHA-256 hash length in hex
        public string? DocumentHash { get; set; }

        public string? UserCryptographicSignature { get; set; } // RSA signature of (Hash + IP + Timestamp)
        
        public string? ManagerCryptographicSignature { get; set; } // RSA signature of (Hash + IP + Timestamp)

        // --- User Signature Metadata ---
        
        [MaxLength(50)]
        public string? UserSignatureMethod { get; set; } // "Draw", "Type", "AdobeSign"
        
        public string? UserSignatureData { get; set; } // Base64 image
        
        [MaxLength(50)]
        public string? UserSignatureIpAddress { get; set; }
        
        public DateTime? UserSignedAt { get; set; }

        // --- Manager Signature Metadata ---
        
        [MaxLength(50)]
        public string? ManagerSignatureMethod { get; set; } // "Draw", "Type", "AdobeSign"
        
        public string? ManagerSignatureData { get; set; } // Base64 image
        
        [MaxLength(50)]
        public string? ManagerSignatureIpAddress { get; set; }
        
        public DateTime? ManagerSignedAt { get; set; }

        // --- Admin Signature Metadata ---
        
        public string? AdminCryptographicSignature { get; set; }
        
        [MaxLength(50)]
        public string? AdminSignatureMethod { get; set; }
        
        public string? AdminSignatureData { get; set; }
        
        [MaxLength(50)]
        public string? AdminSignatureIpAddress { get; set; }
        
        public DateTime? AdminSignedAt { get; set; }
    }
}
