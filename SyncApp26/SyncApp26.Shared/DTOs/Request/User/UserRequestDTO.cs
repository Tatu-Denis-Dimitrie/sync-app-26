using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.Validation;

namespace SyncApp26.Shared.DTOs.Request.User
{
    public class UserRequestDTO
    {
        [StringLength(NameValidationConstants.NameMaxLength, MinimumLength = 1)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "firstName.pattern")]
        public required string FirstName { get; set; }

        [StringLength(NameValidationConstants.NameMaxLength, MinimumLength = 1)]
        [RegularExpression(NameValidationConstants.NamePattern, ErrorMessage = "lastName.pattern")]
        public required string LastName { get; set; }

        public required string Email { get; set; }
        public Guid DepartmentId { get; set; }

        [StringLength(NameValidationConstants.FunctionMaxLength)]
        public string? Function { get; set; }

        public Guid? WorkSiteId { get; set; }

        public Guid? AssignedToId { get; set; }
    }
}
