using System.ComponentModel.DataAnnotations;

namespace SyncApp26.Shared.DTOs.Request.Notification
{
    public class NotificationRequestDTO
    {
        [Required]
        public string DocumentType { get; set; } = string.Empty; // "SSM" or "SU"
    }
}
