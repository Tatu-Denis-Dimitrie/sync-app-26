using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.Validation;

namespace SyncApp26.Shared.DTOs.Request.Organization
{
    public class FunctionRequestDTO
    {
        [StringLength(NameValidationConstants.FunctionMaxLength, MinimumLength = 1)]
        public required string Name { get; set; }
    }
}
