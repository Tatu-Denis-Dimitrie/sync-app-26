using System.ComponentModel.DataAnnotations;

namespace SyncApp26.Shared.DTOs.DataChange
{
    public class CreateDataChangeRequestDTO
    {
        [Required]
        public string RequestedChangesJson { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }
}
