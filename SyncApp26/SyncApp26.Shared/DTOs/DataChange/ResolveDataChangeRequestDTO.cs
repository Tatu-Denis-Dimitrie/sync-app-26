using System.ComponentModel.DataAnnotations;

namespace SyncApp26.Shared.DTOs.DataChange
{
    public class ResolveDataChangeRequestDTO
    {
        [Required]
        [RegularExpression("^(Approved|Rejected)$", ErrorMessage = "status.approvedOrRejected")]
        public string Status { get; set; }
    }
}
