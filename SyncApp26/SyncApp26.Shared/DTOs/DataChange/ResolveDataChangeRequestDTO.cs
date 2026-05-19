using System.ComponentModel.DataAnnotations;

namespace SyncApp26.Shared.DTOs.DataChange
{
    public class ResolveDataChangeRequestDTO
    {
        [Required]
        [RegularExpression("^(Approved|Rejected)$", ErrorMessage = "Status must be 'Approved' or 'Rejected'.")]
        public string Status { get; set; }
    }
}
