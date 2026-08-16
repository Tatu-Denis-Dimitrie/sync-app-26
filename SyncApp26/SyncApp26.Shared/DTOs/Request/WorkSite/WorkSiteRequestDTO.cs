using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.Validation;

namespace SyncApp26.Shared.DTOs.Request.WorkSite
{
    public class WorkSiteRequestDTO
    {
        [StringLength(NameValidationConstants.FunctionMaxLength, MinimumLength = 1)]
        public required string Name { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
