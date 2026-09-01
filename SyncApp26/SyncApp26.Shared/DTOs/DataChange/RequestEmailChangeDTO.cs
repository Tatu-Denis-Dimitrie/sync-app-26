using System.ComponentModel.DataAnnotations;

namespace SyncApp26.Shared.DTOs.DataChange
{
    public class RequestEmailChangeDTO
    {
        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string NewEmail { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Reason { get; set; }
    }
}
